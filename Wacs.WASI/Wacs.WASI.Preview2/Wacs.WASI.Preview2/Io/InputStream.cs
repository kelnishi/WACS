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
    /// Host representation of <c>wasi:io/streams@0.2.3</c>'s
    /// <c>input-stream</c> resource. The class implements
    /// <see cref="IInputStream"/> directly — every method
    /// returns the faithful canonical-ABI <c>result&lt;X,
    /// stream-error&gt;</c> shape, surfacing
    /// <see cref="StreamErrorClosed"/> /
    /// <see cref="StreamErrorLastOperationFailed"/> as Err
    /// values rather than thrown exceptions.
    ///
    /// <para>The default implementation behaves as a closed
    /// stream — every read / skip returns Err(closed). Useful
    /// as a degraded "no input" stand-in; subclasses override
    /// the methods to deliver real data.</para>
    /// </summary>
    [WasiResource("input-stream")]
    public class InputStream : IInputStream, IDisposable
    {
        /// <summary>Read up to <paramref name="len"/> bytes.
        /// Default returns Ok(empty) — guests interpret the
        /// empty list as "no data ready" (non-blocking) rather
        /// than EOF; concrete EOF should surface as
        /// Err(closed).</summary>
        public virtual Result<byte[], StreamError> Read(ulong len)
            => Result<byte[], StreamError>.FromOk(Array.Empty<byte>());

        /// <summary>Block until at least one byte is available,
        /// then read up to <paramref name="len"/>. Default
        /// returns Ok(empty) — same as
        /// <see cref="Read"/>.</summary>
        public virtual Result<byte[], StreamError> BlockingRead(ulong len)
            => Result<byte[], StreamError>.FromOk(Array.Empty<byte>());

        /// <summary>Discard up to <paramref name="len"/> bytes
        /// without copying. Default returns Ok(0).</summary>
        public virtual Result<ulong, StreamError> Skip(ulong len)
            => Result<ulong, StreamError>.FromOk(0UL);

        public virtual Result<ulong, StreamError> BlockingSkip(ulong len)
            => Result<ulong, StreamError>.FromOk(0UL);

        public virtual IPollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }
}
