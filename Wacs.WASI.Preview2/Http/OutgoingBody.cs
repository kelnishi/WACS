// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.outgoing-body</c>.
    /// Write side of an HTTP body — guest pushes bytes via
    /// the <see cref="Write"/> OutputStream.
    ///
    /// <para>Default impl captures every byte the guest writes
    /// into an internal <see cref="System.IO.MemoryStream"/>;
    /// subclasses can read the accumulated bytes via
    /// <see cref="GetBytes"/>. The
    /// <see cref="HttpClientOutgoingHandler"/> uses this to
    /// assemble the request body before
    /// <see cref="HttpClient.SendAsync(HttpRequestMessage,
    /// HttpCompletionOption, System.Threading.CancellationToken)"/>.
    /// </para>
    /// </summary>
    [WasiResource("outgoing-body")]
    public class OutgoingBody : IOutgoingBody, IDisposable
    {
        protected OutputStream? _stream;
        // Backing buffer for the default capturing stream.
        // Subclasses that supply their own _stream simply leave
        // this null and override GetBytes if they want to expose
        // captured bytes through a different mechanism.
        private System.IO.MemoryStream? _captureBuf;

        /// <summary>Take ownership of the write stream.
        /// WIT <c>write: func()
        ///   -&gt; result&lt;own&lt;output-stream&gt;,
        ///                _&gt;</c>.</summary>
        public virtual Result<IOutputStream, Unit> Write()
            => Result<IOutputStream, Unit>.FromOk(WriteConcrete());

        public virtual OutputStream WriteConcrete()
        {
            if (_stream != null) return _stream;
            _captureBuf = new System.IO.MemoryStream();
            _stream = new HostFileOutputStream(_captureBuf);
            return _stream;
        }

        /// <summary>The bytes accumulated through the
        /// outgoing-body's write stream so far. Empty when
        /// nothing was written or when a subclass replaced
        /// the default capturing stream. Used by transports
        /// like <see cref="HttpClientOutgoingHandler"/> to
        /// assemble the request payload.</summary>
        public virtual byte[] GetBytes()
            => _captureBuf?.ToArray()
                ?? System.Array.Empty<byte>();

        /// <summary>Mark the body as complete with optional
        /// trailers. WIT
        /// <c>finish: static func(this: own&lt;outgoing-body&gt;,
        ///   trailers: option&lt;own&lt;trailers&gt;&gt;)
        ///   -&gt; result&lt;_, error-code&gt;</c>.
        /// The generated interface threads <c>this</c> in as
        /// a parameter (static-method semantics); the binder
        /// resolves it from the receiver-handle slot.</summary>
        public virtual Result<Unit, ErrorCode> Finish(
            IOutgoingBody @this, Option<IFields> trailers)
        {
            // Subclasses observe via FinishImpl which keeps
            // the historical Fields? signature for tests that
            // pre-date the generated Option<IFields> param.
            FinishImpl(trailers.HasValue
                ? (Fields)trailers.Value
                : null);
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>Override hook for tests / subclasses.
        /// Default is a no-op.</summary>
        public virtual void FinishImpl(Fields? trailers) { }

        public virtual void Dispose() { }
    }
}
