// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.incoming-body</c>.
    /// Read side of an HTTP body — guest pulls bytes via
    /// the <see cref="Stream"/> InputStream.</summary>
    [WasiResource("incoming-body")]
    public class IncomingBody : IIncomingBody, IDisposable
    {
        protected InputStream? _stream;
        protected FutureTrailers? _trailers;

        /// <summary>Take ownership of the read stream.
        /// WIT <c>%stream: func()
        ///   -&gt; result&lt;own&lt;input-stream&gt;,
        ///                _&gt;</c>.</summary>
        public virtual Result<IInputStream, Unit> Stream()
            => Result<IInputStream, Unit>.FromOk(StreamConcrete());

        public virtual InputStream StreamConcrete()
            => _stream ??= new InputStream();

        /// <summary>Take ownership of the trailers future.
        /// WIT <c>finish: static func(this: own&lt;incoming-body&gt;)
        ///   -&gt; own&lt;future-trailers&gt;</c>. Bare own
        /// return — no result wrapper.</summary>
        public virtual IFutureTrailers Finish(IIncomingBody @this)
            => FinishConcrete();

        public virtual FutureTrailers FinishConcrete()
            => _trailers ??= new FutureTrailers();

        public virtual void Dispose() { }
    }
}
