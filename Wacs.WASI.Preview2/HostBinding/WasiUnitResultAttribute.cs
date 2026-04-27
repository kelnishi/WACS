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
    /// Mark a void-returning method as canon-lowering to WIT
    /// <c>result&lt;_, _&gt;</c> (the <c>result</c> keyword
    /// without type args). This shape canon-lowers as
    /// flat-return i32 — just the outer disc on the wire,
    /// not via retArea memory like
    /// <c>result&lt;T, error-code&gt;</c> does.
    ///
    /// <para>The host method is always-Ok in v0; the binder
    /// returns 0 (Ok) unconditionally after invocation.
    /// Used by WASI HTTP setters whose spec WIT is bare
    /// <c>result</c> (e.g. set-path-with-query, set-method).
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiUnitResultAttribute : Attribute
    {
    }
}
