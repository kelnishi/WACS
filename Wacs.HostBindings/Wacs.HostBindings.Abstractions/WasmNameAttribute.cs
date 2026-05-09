// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.HostBindings.Abstractions
{
    /// <summary>
    /// Carries the original WebAssembly name on an auto-generated
    /// interface method whose CLR identifier was sanitized for
    /// language compatibility.
    ///
    /// <para><b>On IExports methods:</b> the export name as it
    /// appears in the wasm module (e.g.
    /// <c>wasi:cli/run@0.2.0#run</c>). For component command
    /// dispatch, runtimes look for the export named
    /// <c>wasi:cli/run@&lt;version&gt;#run</c> as the canonical
    /// entry — sanitization to a CLR identifier loses the
    /// version digits and structural punctuation, so this
    /// attribute is the round-trip from
    /// <c>wasi_cli_run_0_2_0_run</c> back to the original.</para>
    ///
    /// <para><b>On IImports methods:</b> the import in
    /// <c>{module}:{entity}</c> form (the same pair that
    /// <c>WasmRuntime.BindHostFunction</c> takes). This lets
    /// tooling and host-package resolvers match interface
    /// methods to wasm imports without re-deriving the
    /// sanitization.</para>
    ///
    /// <para>Emitted automatically by the WACS interface
    /// generator on every method of <c>IExports</c> and
    /// <c>IImports</c>. Hand-written types implementing those
    /// interfaces don't need to apply this attribute — only the
    /// generated members carry it.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasmNameAttribute : Attribute
    {
        /// <summary>
        /// The original wasm name. Format depends on whether the
        /// attribute decorates an IExports or IImports method —
        /// see the type-level docs.
        /// </summary>
        public string Name { get; }

        public WasmNameAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
