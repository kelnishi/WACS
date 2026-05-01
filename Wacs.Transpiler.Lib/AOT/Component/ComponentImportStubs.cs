// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Registers throwing-stub <see cref="IFunctionInstance"/>s for
    /// every import a parsed core module declares. Used by the
    /// component-mode transpiler path when the caller wants a
    /// `configureImports` callback that satisfies the runtime's
    /// instantiation-time validation without having to type each
    /// import's delegate by hand.
    ///
    /// <para>Direct-linked imports never invoke through these stubs —
    /// the call-site IL bypasses the delegate table entirely. Stubs
    /// only matter for unresolved imports, which a correct
    /// `--wasip2` / `--host-package` transpile shouldn't have. If a
    /// stub IS invoked, the throw surfaces the unbound-import bug
    /// loudly with the (module, entity) name in the message.</para>
    /// </summary>
    public static class ComponentImportStubs
    {
        /// <summary>
        /// Walk <paramref name="coreModule"/>'s function imports and
        /// bind each one in <paramref name="runtime"/> to a stub that
        /// throws on invocation. Non-function imports
        /// (memory / table / global / tag) are skipped — those don't
        /// route through the delegate table.
        /// </summary>
        public static void RegisterAll(WasmRuntime runtime, Module coreModule)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (coreModule == null) throw new ArgumentNullException(nameof(coreModule));

            // Unroll once — DefType.Expansion gives the FunctionType
            // for func type indices.
            var defs = coreModule.UnrollTypes();

            foreach (var import in coreModule.Imports)
            {
                if (!(import.Desc is Module.ImportDesc.FuncDesc fd))
                    continue;
                var defType = defs[fd.TypeIndex.Value];
                if (!(defType.Expansion is FunctionType funcType))
                    continue;
                runtime.BindHostFunction(
                    (import.ModuleName, import.Name),
                    new ThrowingHostFunction(
                        import.ModuleName, import.Name, funcType));
            }
        }

        // IFunctionInstance shape that throws when invoked. Carries
        // the (module, entity) name so the exception message points
        // at the unresolved import that escaped direct linking.
        private sealed class ThrowingHostFunction : IFunctionInstance
        {
            private readonly string _moduleName;
            private readonly string _entityName;

            public ThrowingHostFunction(string moduleName, string entityName,
                FunctionType type)
            {
                _moduleName = moduleName;
                _entityName = entityName;
                Type = type;
            }

            public FunctionType Type { get; }
            public string Id => _moduleName + "." + _entityName;
            public bool IsAsync => false;
            public string Name => _entityName;
            public bool IsExport { get; set; }
            public void SetName(string name) { }

            public void Invoke(ExecContext context) =>
                throw new InvalidOperationException(
                    "Unresolved component import '" + _moduleName + "." + _entityName
                    + "' was invoked at runtime. The transpile-time host package "
                    + "did not bind it; either supply a wider --host-package or "
                    + "register a real binding via configureImports.");
        }
    }
}
