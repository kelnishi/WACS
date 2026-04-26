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
    /// <c>result&lt;T, stream-error&gt;</c> return — the WIT
    /// shape used pervasively by wasi:io/streams' methods.
    /// The host method's declared return type is the Ok
    /// payload (void → result&lt;_, stream-error&gt;,
    /// <see cref="ulong"/> → result&lt;u64, stream-error&gt;,
    /// <see cref="byte"/>[] → result&lt;list&lt;u8&gt;,
    /// stream-error&gt;).
    ///
    /// <para>v0 always emits Ok — throwing from the host
    /// method propagates as a wasm trap rather than mapping
    /// to <c>last-operation-failed</c> / <c>closed</c>. The
    /// Err encoding is a follow-up that requires Error
    /// resource handle allocation in the wrapper.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiStreamResultAttribute : Attribute
    {
    }
}
