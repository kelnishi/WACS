// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Host representation of <c>wasi:io/streams@0.2.8</c>'s
    /// <c>output-stream</c> resource. Implements
    /// <see cref="IOutputStream"/> directly — every method
    /// returns the canonical-ABI <c>result&lt;X,
    /// stream-error&gt;</c> shape; concrete failures surface as
    /// Err(<see cref="StreamError"/>) rather than thrown
    /// exceptions.
    ///
    /// <para>The default implementation is a permissive sink:
    /// <see cref="CheckWrite"/> returns Ok(64K) so guests can
    /// always write; the write methods discard contents and
    /// return Ok(()). Subclasses override
    /// <see cref="Write"/> / <see cref="BlockingWriteAndFlush"/>
    /// to actually consume the bytes (e.g. into stdout, a
    /// file, or a network socket).</para>
    /// </summary>
    [WasiResource("output-stream")]
    public class OutputStream : IOutputStream, IDisposable
    {
        /// <summary>Maximum bytes the host can accept right now
        /// without blocking. Default: 64K — most non-network
        /// sinks accept any reasonable buffer.</summary>
        public virtual Result<ulong, StreamError> CheckWrite()
            => Result<ulong, StreamError>.FromOk(65_536UL);

        /// <summary>Write <paramref name="contents"/>. Default
        /// discards.</summary>
        public virtual Result<Unit, StreamError> Write(byte[] contents)
            => Result<Unit, StreamError>.FromOk(Unit.Value);

        /// <summary>Block until <paramref name="contents"/> is
        /// fully written + flushed.</summary>
        public virtual Result<Unit, StreamError> BlockingWriteAndFlush(
            byte[] contents)
            => Result<Unit, StreamError>.FromOk(Unit.Value);

        /// <summary>Trigger flush of any buffered bytes.</summary>
        public virtual Result<Unit, StreamError> Flush()
            => Result<Unit, StreamError>.FromOk(Unit.Value);

        /// <summary>Block until all buffered bytes are
        /// flushed.</summary>
        public virtual Result<Unit, StreamError> BlockingFlush()
            => Result<Unit, StreamError>.FromOk(Unit.Value);

        public virtual IPollable Subscribe() => new Pollable();

        /// <summary>Write <paramref name="len"/> zero bytes to
        /// the sink.</summary>
        public virtual Result<Unit, StreamError> WriteZeroes(ulong len)
            => Result<Unit, StreamError>.FromOk(Unit.Value);

        /// <summary>Block until <paramref name="len"/> zero
        /// bytes are written and flushed.</summary>
        public virtual Result<Unit, StreamError>
            BlockingWriteZeroesAndFlush(ulong len)
            => Result<Unit, StreamError>.FromOk(Unit.Value);

        /// <summary>Move up to <paramref name="len"/> bytes from
        /// <paramref name="src"/> straight into this output —
        /// guests use this to chain a read-stream to a write-
        /// stream without bouncing through their own buffer.
        /// Default impl reads from <paramref name="src"/> via
        /// the IInputStream contract and writes to <c>this</c>;
        /// either side's Err short-circuits the
        /// pipeline.</summary>
        public virtual Result<ulong, StreamError> Splice(
            IInputStream src, ulong len)
        {
            var read = src.Read(len);
            if (read.IsErr)
                return Result<ulong, StreamError>.FromErr(read.Err);
            var bytes = read.Ok;
            var write = Write(bytes);
            if (write.IsErr)
                return Result<ulong, StreamError>.FromErr(write.Err);
            return Result<ulong, StreamError>.FromOk((ulong)bytes.Length);
        }

        /// <summary>Like <see cref="Splice"/> but blocks until
        /// at least one byte moves.</summary>
        public virtual Result<ulong, StreamError> BlockingSplice(
            IInputStream src, ulong len)
        {
            var read = src.BlockingRead(len);
            if (read.IsErr)
                return Result<ulong, StreamError>.FromErr(read.Err);
            var bytes = read.Ok;
            var write = Write(bytes);
            if (write.IsErr)
                return Result<ulong, StreamError>.FromErr(write.Err);
            return Result<ulong, StreamError>.FromOk((ulong)bytes.Length);
        }

        public virtual void Dispose() { }
    }
}
