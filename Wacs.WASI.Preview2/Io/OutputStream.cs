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
    /// Host representation of <c>wasi:io/streams@0.2.x</c>'s
    /// <c>output-stream</c> resource. Subset of the WIT
    /// surface that ships in v0:
    /// <code>
    /// resource output-stream {
    ///     check-write: func() -&gt; result&lt;u64, stream-error&gt;;
    ///     write: func(contents: list&lt;u8&gt;) -&gt; result&lt;_, stream-error&gt;;
    ///     blocking-write-and-flush: func(contents: list&lt;u8&gt;) -&gt; result&lt;_, stream-error&gt;;
    ///     flush: func() -&gt; result&lt;_, stream-error&gt;;
    ///     blocking-flush: func() -&gt; result&lt;_, stream-error&gt;;
    ///     subscribe: func() -&gt; own&lt;pollable&gt;;
    /// }
    /// </code>
    ///
    /// <para>Always-Ok semantics in v0 — host methods return
    /// void / u64 directly; the wrapper writes Ok with the
    /// payload (or no payload). Throwing from a host method
    /// propagates back through wasm — the StreamError variant
    /// payload form (last-operation-failed / closed) is a
    /// follow-up.</para>
    ///
    /// <para>The default impl is "no-op" — guests wire it as
    /// stdout/stderr; subclasses override <see cref="Write"/>
    /// to actually consume the bytes.</para>
    /// </summary>
    [WasiResource("output-stream")]
    public class OutputStream : IOutputStream, IDisposable
    {
        /// <summary>Maximum bytes the host can accept right
        /// now without blocking. Default: large constant —
        /// most non-network sinks are happy to take any
        /// reasonable buffer.</summary>
        public virtual ulong CheckWrite() => 65_536UL;

        /// <summary>Write <paramref name="contents"/> to the
        /// underlying sink. Default: discard.</summary>
        public virtual void Write(byte[] contents) { }

        /// <summary>Block until <paramref name="contents"/> is
        /// fully written + flushed. Default: same as
        /// <see cref="Write"/> for the no-op impl.</summary>
        public virtual void BlockingWriteAndFlush(byte[] contents) { }

        /// <summary>Trigger flush of any buffered bytes.
        /// Default: no-op.</summary>
        public virtual void Flush() { }

        /// <summary>Block until all buffered bytes are flushed.
        /// Default: no-op.</summary>
        public virtual void BlockingFlush() { }

        /// <summary>Write <paramref name="len"/> zero bytes to
        /// the sink. Default: discard.</summary>
        public virtual void WriteZeroes(ulong len) { }

        /// <summary>Block until <paramref name="len"/> zero
        /// bytes are written and flushed.</summary>
        public virtual void BlockingWriteZeroesAndFlush(ulong len) { }

        /// <summary>Move up to <paramref name="len"/> bytes from
        /// <paramref name="src"/> straight into this output —
        /// guests use this to chain a read-stream to a write-
        /// stream without bouncing through their own buffer.
        /// Default impl reads from <paramref name="src"/> and
        /// writes to <c>this</c>, returning the count actually
        /// moved.</summary>
        public virtual ulong Splice(InputStream src, ulong len)
        {
            var bytes = src.Read(len);
            Write(bytes);
            return (ulong)bytes.Length;
        }

        /// <summary>Like <see cref="Splice"/> but blocks until
        /// at least one byte moves.</summary>
        public virtual ulong BlockingSplice(InputStream src, ulong len)
        {
            var bytes = src.BlockingRead(len);
            Write(bytes);
            return (ulong)bytes.Length;
        }

        /// <summary>Subscribe to a pollable signaling when the
        /// next write won't block. Default: always-ready
        /// pollable.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }

        // ----- IOutputStream (faithful Result-returning shape) -----
        // Phase B4 shortcut: the hand-written OutputStream uses a
        // throw-on-error idiom. Explicit-interface stubs throw
        // NotImplementedException; bindings continue calling the
        // virtual non-Result methods above. See InputStream.cs
        // for the rationale.
        Result<ulong, StreamError> IOutputStream.CheckWrite() =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call CheckWrite() directly.");

        Result<Unit, StreamError> IOutputStream.Write(byte[] contents) =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call Write(byte[]) directly.");

        Result<Unit, StreamError> IOutputStream.BlockingWriteAndFlush(byte[] contents) =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call BlockingWriteAndFlush(byte[]) directly.");

        Result<Unit, StreamError> IOutputStream.Flush() =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call Flush() directly.");

        Result<Unit, StreamError> IOutputStream.BlockingFlush() =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call BlockingFlush() directly.");

        IPollable IOutputStream.Subscribe() => Subscribe();

        Result<Unit, StreamError> IOutputStream.WriteZeroes(ulong len) =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call WriteZeroes(ulong) directly.");

        Result<Unit, StreamError> IOutputStream.BlockingWriteZeroesAndFlush(ulong len) =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call BlockingWriteZeroesAndFlush(ulong) directly.");

        Result<ulong, StreamError> IOutputStream.Splice(IInputStream src, ulong len) =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call Splice(InputStream, ulong) directly.");

        Result<ulong, StreamError> IOutputStream.BlockingSplice(IInputStream src, ulong len) =>
            throw new NotImplementedException(
                "OutputStream uses throw-on-error idiom; bindings call BlockingSplice(InputStream, ulong) directly.");
    }
}
