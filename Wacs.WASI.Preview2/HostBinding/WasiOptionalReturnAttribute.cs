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
    /// Marks a host method as returning an
    /// <c>option&lt;…&gt;</c> rather than the equivalent non-
    /// nullable WIT type. Mostly used for
    /// <c>option&lt;string&gt;</c> — C# can't distinguish
    /// <c>string?</c> from <c>string</c> at the reflection
    /// level, so the binder needs an explicit opt-in.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiOptionalReturnAttribute : Attribute
    {
    }
}
