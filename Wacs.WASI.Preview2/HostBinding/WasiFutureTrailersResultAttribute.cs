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
    /// <c>option&lt;result&lt;option&lt;own&lt;trailers&gt;&gt;,
    /// error-code&gt;&gt;</c> — the future-trailers.get shape.
    /// The host method's C# return type must be
    /// <c>(bool ready, Fields? trailers)</c>:
    /// <list type="bullet">
    /// <item>(false, null) → outer None (still pending)</item>
    /// <item>(true, null) → Some(Ok(None)) (ready, no trailers)</item>
    /// <item>(true, fields) → Some(Ok(Some(handle))) (ready
    /// with trailers)</item>
    /// </list>
    /// v0 always-Ok — the error-code Err side is left zeroed
    /// (the guest's cabi_realloc'd buffer arrives zeroed).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiFutureTrailersResultAttribute : Attribute
    {
    }
}
