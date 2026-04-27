// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Mark a host method whose canon-lower target uses the
    /// FULL WIT <c>wasi:http/types.error-code</c> variant
    /// (39 cases, max_align = 8 due to <c>option&lt;u64&gt;</c>
    /// payloads). The binder shifts retArea offsets to
    /// accommodate the larger Err payload area:
    /// <list type="bullet">
    /// <item>Outer disc at retAreaPtr+0 (1B), padded to +8</item>
    /// <item>Ok payload at retAreaPtr+8 (instead of +4 used
    /// by simplified single-case error-code WIT)</item>
    /// <item>Err side (when the host throws
    /// <see cref="WasiErrorCodeException"/>) writes disc=1
    /// + the 32-byte variant slot via
    /// <see cref="Wacs.WASI.Preview2.Http.ErrorCodeEncoder"/>
    /// at retAreaPtr+8</item>
    /// </list>
    ///
    /// <para>Without this attribute, error-result binders
    /// stay on the simplified-WIT layout (Ok payload at +4,
    /// retArea = 8 bytes total) and treat host throws as
    /// bugs. This split lets v0 fixtures (which use a one-
    /// case <c>variant error-code { internal-error }</c> WIT
    /// for layout simplicity) keep working unchanged while
    /// real-spec hosts opt in to the full encoder.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiSpecErrorCodeAttribute : Attribute
    {
    }
}
