// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.Http;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Host-side error signal for the WIT
    /// <c>error-code</c> variant. Throw from a host method
    /// whose binder is marked
    /// <see cref="WasiSpecErrorCodeAttribute"/> to write the
    /// Err side of a <c>result&lt;X, error-code&gt;</c> return.
    /// The binder catches, encodes via
    /// <see cref="ErrorCodeEncoder"/>, and writes the
    /// outer disc=1 + variant payload into the retArea.
    ///
    /// <para>Default error-result paths (without
    /// [WasiSpecErrorCode]) treat throws as host bugs and
    /// let them propagate; only methods opted in to spec-
    /// layout encoding catch this exception type.</para>
    /// </summary>
    public sealed class WasiErrorCodeException : Exception
    {
        /// <summary>The ErrorCode case to encode on the wire.
        /// </summary>
        public ErrorCode Code { get; }

        public WasiErrorCodeException(ErrorCode code)
            : base("WASI error-code: "
                  + (code?.GetType().Name ?? "<null>"))
        {
            Code = code ?? throw new ArgumentNullException(
                nameof(code));
        }

        public WasiErrorCodeException(ErrorCode code,
            string message)
            : base(message)
        {
            Code = code ?? throw new ArgumentNullException(
                nameof(code));
        }
    }
}
