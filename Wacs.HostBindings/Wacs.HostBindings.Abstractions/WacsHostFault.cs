// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;

namespace Wacs.HostBindings
{
    /// <summary>
    /// Thrown by host bindings to surface a wasm-runtime-level fault.
    /// Used by <see cref="WacsHostMemory"/> for out-of-bounds memory access
    /// and intended for binding authors to wrap any I/O failure that should
    /// trap the guest.
    ///
    /// <para>The transpiled module's caller catches this and converts it
    /// to a wasm trap (via the runtime's trap-propagation machinery); for
    /// AOT-linked builds, an uncaught WacsHostFault terminates the process
    /// the same way a transpiled-IL exception would.</para>
    /// </summary>
    public class WacsHostFault : Exception
    {
        public WacsHostFault(string message) : base(message) { }
        public WacsHostFault(string message, Exception inner) : base(message, inner) { }
    }
}
