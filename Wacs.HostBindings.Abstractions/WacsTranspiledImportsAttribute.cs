// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;

namespace Wacs.HostBindings
{
    /// <summary>
    /// Identifies the <c>IImports</c> interface a transpiled wasm module
    /// expects its host to provide. Emitted by <c>WACS.Transpiler</c> as an
    /// assembly-level attribute on every saved .dll, so consumers can
    /// reflect on the .dll without having to know its namespace conventions
    /// (which the user picks via <c>--namespace</c> / <c>--module</c>).
    ///
    /// <para>Consumed by <c>WACS.HostBindings.SourceGen</c> at consumer
    /// build time: when the generator scans the consumer's compilation
    /// references and finds an assembly carrying this attribute, it emits
    /// a <c>GeneratedHostImports</c> partial class implementing the named
    /// interface, with each method delegating to a matching
    /// <c>[WacsImport]</c>-annotated static.</para>
    ///
    /// <para>Multiple transpiled modules in one consumer compilation are
    /// supported; the source generator emits one <c>GeneratedHostImports</c>
    /// per discovered interface, disambiguating by namespace.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WacsTranspiledImportsAttribute : Attribute
    {
        public Type ImportsInterface { get; }

        public WacsTranspiledImportsAttribute(Type importsInterface)
        {
            ImportsInterface = importsInterface ?? throw new ArgumentNullException(nameof(importsInterface));
        }
    }
}
