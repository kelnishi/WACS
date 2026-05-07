// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.NN.Types
{
    /// <summary>
    /// Backend-side signaling exception that carries a typed
    /// <see cref="ErrorCode"/> plus a human-readable message.
    /// The WIT binding lifts this into the <c>error</c> resource
    /// returned through <c>result&lt;_, error&gt;</c>; the WITX
    /// binding maps the code through
    /// <see cref="ErrorCodeMapping.ToWitx"/> and returns the u16.
    ///
    /// <para>Backends should throw this rather than returning an
    /// in-band sentinel — the symmetric WIT/WITX bindings handle
    /// the wire mapping centrally so every backend gets
    /// uniform error behavior across both ABIs.</para>
    /// </summary>
    public sealed class WasiNNException : Exception
    {
        public ErrorCode Code { get; }

        /// <summary>
        /// Backend-specific status string surfaced through the
        /// WIT <c>error.data</c> accessor. Named <c>BackendData</c>
        /// (not <c>Data</c>) to avoid hiding
        /// <see cref="Exception.Data"/> on the base class. When
        /// null, the WIT binding returns
        /// <see cref="Exception.Message"/> as the data field —
        /// empty/null is fine; the WIT spec allows any string.
        /// </summary>
        public string? BackendData { get; }

        public WasiNNException(ErrorCode code, string message,
            string? backendData = null, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            BackendData = backendData;
        }
    }
}
