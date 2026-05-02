// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Lookup table for per-delegate-type call_indirect / call_ref
    /// dispatch shims. Every shim is a static method emitted at
    /// transpile time by
    /// <c>ModuleClassGenerator.EmitTypedDelegateShimsAndRegister</c>:
    /// it casts the Delegate to the concrete Func/Action type, unboxes
    /// each <c>object?</c> arg, calls Invoke directly, and boxes the
    /// return value.
    ///
    /// <para>Older revisions of this class also emitted shims at runtime
    /// via <see cref="System.Reflection.Emit.DynamicMethod"/> (with a
    /// reflection-based fallback under PublishAot where Reflection.Emit
    /// is unavailable). Both fallback paths were proven dead by the
    /// instrumented test pass that landed in the same commit set as the
    /// transpile-time emission — every transpiled-module dispatch path
    /// in Wacs.Transpiler.Test (730/730), Spec.Test (782/784), the
    /// Wacs.WASI.Preview1.Test suite, and the AOT smoke + coremark
    /// scenarios goes through a registered shim. The fallback bodies
    /// were removed; <see cref="GetOrBuild"/> now throws if asked for
    /// an unregistered type so any future coverage gap surfaces loudly.</para>
    /// </summary>
    public static class TypedDelegateInvoker
    {
        public delegate object? Invoker(Delegate del, object?[] args);

        private static readonly ConcurrentDictionary<Type, Invoker> _cache = new();

        /// <summary>
        /// Register a precompiled shim for <paramref name="delegateType"/>.
        /// Called from a transpiled module's ctor IL with a static method
        /// emitted by <c>ModuleClassGenerator</c> at transpile time.
        /// Idempotent — re-registration from a second module overwrites
        /// the first with an equivalent shim (the IL is determined
        /// entirely by the delegate signature).
        /// </summary>
        public static void RegisterShim(Type delegateType, Invoker shim)
        {
            if (delegateType == null) throw new ArgumentNullException(nameof(delegateType));
            if (shim == null) throw new ArgumentNullException(nameof(shim));
            _cache[delegateType] = shim;
        }

        /// <summary>
        /// Look up the shim registered for <paramref name="delegateType"/>.
        /// Throws if no shim was registered — every dispatch site
        /// reachable from a transpiled module is covered by
        /// <c>ModuleClassGenerator</c>'s shim emission, so a miss here
        /// is a transpiler bug or an out-of-band caller.
        /// </summary>
        public static Invoker GetOrBuild(Type delegateType)
        {
            if (_cache.TryGetValue(delegateType, out var invoker))
                return invoker;
            throw new InvalidOperationException(
                $"No call_indirect dispatch shim registered for {delegateType.FullName}. "
                + "Every signature reachable from a transpiled module is registered by "
                + "ModuleClassGenerator.EmitTypedDelegateShimsAndRegister at module-construction "
                + "time. A miss here means either the wasm has a function signature with >16 "
                + "parameters (unsupported — BuildDelegateType returns null), or this "
                + "GetOrBuild is being called from outside the transpiler path.");
        }
    }
}
