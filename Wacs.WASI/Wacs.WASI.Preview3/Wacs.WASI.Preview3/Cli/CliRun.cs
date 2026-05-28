// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview3.Cli
{
    /// <summary>
    /// Host-side helper for invoking a component's exported
    /// <c>wasi:cli/run.run</c>:
    /// <code>
    /// run: async func() -&gt; result;
    /// </code>
    /// The host calls this on the component; the component runs
    /// asynchronously and returns a CLR <see cref="Task{T}"/>
    /// the host awaits. Errors surface as a faulted task.
    ///
    /// <para><b>Wiring</b>: once an end-to-end fixture is
    /// available, this class loads the component, instantiates
    /// it through <c>ComponentInstance.Instantiate</c> (the shim
    /// recognizer handles canon-async wire convention), looks
    /// up the exported <c>run</c> via the component's export
    /// interface, calls it, and surfaces the task. The exact
    /// interop shape depends on wit-component's emit for the
    /// <c>async func()</c> export — pinned at Slice J once
    /// fixture lands.</para>
    /// </summary>
    public static class CliRun
    {
        /// <summary>
        /// Placeholder. Throws <see cref="NotImplementedException"/>
        /// with a clear "awaits fixture" diagnostic. The shape
        /// here pins the public-API contract — when the
        /// fixture-driven implementation lands, this signature
        /// stays stable.
        /// </summary>
        public static Task<int> InvokeRunAsync(
            object componentInstance)
        {
            throw new NotImplementedException(
                "wasi:cli/run.run end-to-end invocation awaits a " +
                "wit-component-emitted .component.wasm fixture for " +
                "the wasi-0.3.0-rc-2026-03-15 cli/run export. The " +
                "canon-async substrate (WACS.ComponentModel " +
                "0.8.13+) covers the read side; this layer pins " +
                "the host-facing public-API shape.");
        }
    }
}
