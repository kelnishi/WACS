// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>Stub for the WIT
    /// <c>wasi:http/types.error-code</c> variant. v0 only
    /// surfaces this type as the None side of
    /// <c>option&lt;error-code&gt;</c> returns — the full 39-
    /// case variant hierarchy + payload encoder is v1 work.
    /// Cannot be instantiated.</summary>
    public abstract class ErrorCode
    {
        private protected ErrorCode() { }
    }
}
