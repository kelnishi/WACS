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
    /// Mark a static factory method on a [WasiResource] class
    /// as the WIT <c>constructor</c>. The auto-binder registers
    /// it under the <c>[constructor]Resource</c> import name
    /// (no trailing <c>.method-name</c> — constructors don't
    /// have one). Wire form: declared parameters in order,
    /// returning a fresh resource handle (i32).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiConstructorAttribute : Attribute
    {
    }
}
