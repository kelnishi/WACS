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
    /// Mark a resource method as a WIT
    /// <c>static func(this: own&lt;self&gt;, …)</c>. The
    /// auto-binder registers it under the
    /// <c>[static]Resource.method</c> import-name prefix
    /// instead of <c>[method]Resource.method</c>. Wire form
    /// is otherwise identical — the C# method's implicit
    /// <c>this</c> resolves the resource handle from the
    /// first wire slot, same as an instance method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiStaticMethodAttribute : Attribute
    {
    }
}
