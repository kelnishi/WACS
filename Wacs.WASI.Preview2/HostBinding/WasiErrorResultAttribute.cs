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
    /// Marks a host method as canon-lowering to a
    /// <c>result&lt;T, error-code&gt;</c> return — the WIT
    /// pattern used by wasi:sockets/* (tcp-create-socket,
    /// etc.) and most of wasi:filesystem's descriptor
    /// methods. T is the host method's declared return type
    /// (typically a resource); error-code is a no-payload
    /// variant whose 1-byte discriminator carries the failure
    /// reason.
    ///
    /// <para>v0 always emits Ok — host throwing propagates as
    /// wasm trap. Full Err encoding (mapping host
    /// <see cref="System.Exception"/> kinds to WASI error
    /// codes) is a follow-up.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiErrorResultAttribute : Attribute
    {
    }
}
