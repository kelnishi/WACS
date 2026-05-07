// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0.

using Microsoft.CodeAnalysis;

namespace Wacs.HostBindings.SourceGen
{
    internal static class Diagnostics
    {
        public static readonly DiagnosticDescriptor UnmatchedImport = new(
            id: "WACS001",
            title: "No host binding for wasm import",
            messageFormat: "No [WacsImport(\"{0}\", \"{1}\")] static method found in any referenced assembly. " +
                           "The transpiled module's IImports interface has a method '{2}' that maps to this " +
                           "wasm import. Add the binding (e.g. via WACS.WASI.Preview1) or implement it locally. " +
                           "The generated adapter throws WacsHostFault if the method is invoked.",
            category: "Wacs.HostBindings",
            // Warning, not error — component-mode wasm direct-links its
            // imports at transpile time, so the IImports methods exist for
            // type-system completeness but are never called at runtime. A
            // hard error there would block the build for no functional
            // reason. Embedders who consume IImports directly (core wasm
            // wired via the source-generated adapter) can promote this to
            // an error via /warnaserror:WACS001 or .editorconfig.
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SignatureMismatch = new(
            id: "WACS002",
            title: "Host binding signature mismatch",
            messageFormat: "Binding [WacsImport(\"{0}\", \"{1}\")] on '{2}' has parameter types incompatible with " +
                           "the IImports method '{3}'. Expected wasm-typed args ({4}), got binding args ({5}).",
            category: "Wacs.HostBindings",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AmbiguousBinding = new(
            id: "WACS003",
            title: "Ambiguous host bindings for wasm import",
            messageFormat: "Multiple [WacsImport(\"{0}\", \"{1}\")] static methods found in referenced assemblies " +
                           "({2}). Remove duplicates or pick one explicitly.",
            category: "Wacs.HostBindings",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MatchedBinding = new(
            id: "WACS004",
            title: "Host binding matched",
            messageFormat: "Wasm import '{0}.{1}' bound to {2}",
            category: "Wacs.HostBindings",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: false);
    }
}
