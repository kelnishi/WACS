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
    /// Mark a resource method as canon-lowering to WIT
    /// <c>option&lt;result&lt;result&lt;own&lt;incoming-response&gt;,
    /// error-code&gt;, _&gt;&gt;</c> — the future-incoming-
    /// response.get shape. Different from
    /// <see cref="WasiFutureTrailersResultAttribute"/> in
    /// that the inner type is <c>result&lt;own&lt;X&gt;,
    /// error-code&gt;</c> (not <c>option&lt;own&lt;X&gt;&gt;</c>),
    /// and the outer Err side is bare unit (representing
    /// "already consumed", not an error-code).
    ///
    /// <para>Host method's C# return type must be
    /// <c>(bool ready, IncomingResponse? response)</c>:
    /// <list type="bullet">
    /// <item>(false, _) → outer None (still pending)</item>
    /// <item>(true, response) → ready with response handle
    /// (non-null required when ready=true)</item>
    /// </list>
    /// v0 always-Ok — outer Err (already-consumed) and inner
    /// Err (error-code) sides are not surfaced; they leave
    /// the buffer zeroed.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiFutureIncomingResponseResultAttribute : Attribute
    {
    }
}
