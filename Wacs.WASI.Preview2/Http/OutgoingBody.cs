// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.outgoing-body</c>.
    /// Write side of an HTTP body — guest pushes bytes via
    /// the <see cref="Write"/> OutputStream.</summary>
    [WasiResource("outgoing-body")]
    public class OutgoingBody : IDisposable
    {
        protected OutputStream? _stream;

        /// <summary>Take ownership of the write stream.
        /// WIT <c>write: func()
        ///   -&gt; result&lt;own&lt;output-stream&gt;,
        ///                _&gt;</c>.</summary>
        public virtual OutputStream Write()
            => _stream ??= new OutputStream();

        /// <summary>Mark the body as complete with optional
        /// trailers. WIT
        /// <c>finish: static func(this: own&lt;outgoing-body&gt;,
        ///   trailers: option&lt;own&lt;trailers&gt;&gt;)
        ///   -&gt; result&lt;_, error-code&gt;</c>.
        /// At the wire level, an instance method's first
        /// param IS the resource handle, so this can stay an
        /// instance method on the C# class — the
        /// attribute just changes the
        /// import-name prefix from [method] to [static].
        /// </summary>
        public virtual void Finish(
            Fields? trailers) { }

        public virtual void Dispose() { }
    }
}
