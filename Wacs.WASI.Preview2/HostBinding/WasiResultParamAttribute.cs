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
    /// Mark a parameter as the Ok-side of a WIT
    /// <c>result&lt;own&lt;T&gt;, E&gt;</c> param. The C#
    /// type is the resolved <c>T?</c> (resource); the binder
    /// reads the wire 2-slot pair (outer disc + handle), and
    /// when disc==0 resolves the handle through the resource
    /// table; otherwise hands the host method <c>null</c>.
    /// The Err side's payload is not surfaced in v0 — always-
    /// Ok semantics; the host method gets one resolved value
    /// or null.
    /// Used by <c>response-outparam.set</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiResultParamAttribute : Attribute
    {
    }
}
