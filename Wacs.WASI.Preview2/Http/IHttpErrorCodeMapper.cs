// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT top-level
    /// <c>wasi:http/types.http-error-code: func(
    ///   err: borrow&lt;io-error&gt;) -&gt; option&lt;error-code&gt;</c>.
    /// Translates an opaque io-error resource handle into an
    /// HTTP-shaped error-code if applicable. v0 always
    /// returns None — no WACS-side mapping yet.</summary>
    public interface IHttpErrorCodeMapper
    {
        [WasiOptionalReturn]
        [WasiMethodName("http-error-code")]
        ErrorCode? HttpErrorCode(Wacs.WASI.Preview2.Io.Error err);
    }

    /// <summary>Default <see cref="IHttpErrorCodeMapper"/> impl
    /// — always returns None. Concrete hosts override to
    /// surface specific error-code mappings when their stream
    /// implementation produces a known error class.</summary>
    public sealed class HttpErrorCodeMapperSource : IHttpErrorCodeMapper
    {
        [WasiOptionalReturn]
        [WasiMethodName("http-error-code")]
        public ErrorCode? HttpErrorCode(Wacs.WASI.Preview2.Io.Error err)
            => null;
    }
}
