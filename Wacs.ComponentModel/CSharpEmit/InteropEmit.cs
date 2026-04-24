// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Emits wit-bindgen-csharp-shaped Interop files — the
    /// <c>{InterfaceName}Interop.cs</c> sources that contain
    /// <c>[DllImport]</c> stubs plus user-facing wrapper functions
    /// for every free function (not resource method) an imported
    /// interface declares. Verified against
    /// <c>wit-bindgen-csharp 0.30.0</c> output over two synthetic
    /// WIT fixtures (primitives + signed/bool/char/u64/f32 cases).
    ///
    /// <para><b>Phase 1a.2 scope:</b> free functions with
    /// primitive-typed parameters and primitive-or-void returns.
    /// Aggregates (<c>list</c>, <c>option</c>, <c>result</c>,
    /// <c>tuple</c>) and resource-typed signatures are follow-ups
    /// — they need either canonical-ABI marshaling emission (lists,
    /// strings, records) or the resource-class emitter (own/borrow
    /// handles).</para>
    /// </summary>
    internal static class InteropEmit
    {
        /// <summary>
        /// Build a full <c>{InterfaceName}Interop.cs</c> source for
        /// an imported interface whose every free function uses only
        /// primitive types. Caller must ensure emittability via
        /// <see cref="CSharpEmitter.IsInterfaceInteropEmitable"/>.
        /// </summary>
        public static string EmitImportInteropContent(
            CtInterfaceType iface,
            string worldNameKebab)
        {
            if (iface.Package == null)
                throw new ArgumentException(
                    "Interop emission requires a packaged interface.",
                    nameof(iface));

            var ifaceNs = NameConventions.InterfaceNamespace(
                worldNameKebab, isExport: false, iface.Package);
            var className = NameConventions.ToPascalCase(iface.Name) + "Interop";
            var entryPointBase = BuildEntryPointBase(iface);

            var sb = new StringBuilder();
            sb.Append(CSharpEmitter.Header);
            sb.Append("namespace ").Append(ifaceNs).Append("\n{\n");
            sb.Append("    public static class ").Append(className).Append(" {\n\n");

            foreach (var fn in iface.Functions)
            {
                EmitFreeFunction(sb, fn, entryPointBase);
            }

            sb.Append("    }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Build the <c>DllImport</c> EntryPoint base: for
        /// <c>wasi:cli/stdout@0.2.3</c> the base is exactly that
        /// string; free-function stubs append <c>, EntryPoint =
        /// "{wit-func-name}"</c> around it. Versionless packages
        /// drop the <c>@</c> suffix.
        /// </summary>
        private static string BuildEntryPointBase(CtInterfaceType iface)
        {
            // `{ns}:{path0}:{path1}.../{iface-name}` — colon-joined
            // namespace + path segments, then `/` before interface.
            var sb = new StringBuilder();
            sb.Append(iface.Package!.Namespace);
            foreach (var seg in iface.Package.Path)
            {
                sb.Append(':');
                sb.Append(seg);
            }
            sb.Append('/');
            sb.Append(iface.Name);
            if (iface.Package.Version != null)
            {
                sb.Append('@');
                sb.Append(iface.Package.Version);
            }
            return sb.ToString();
        }

        // ---- Per-function emission ----------------------------------------

        private static void EmitFreeFunction(StringBuilder sb,
                                             CtInterfaceFunction fn,
                                             string entryPointBase)
        {
            var methodName = NameConventions.ToPascalCase(fn.Name);
            var stubClassName = methodName + "WasmInterop";

            // Inner stub class with DllImport.
            sb.Append("        internal static class ").Append(stubClassName).Append('\n');
            sb.Append("        {\n");
            sb.Append("            [DllImport(\"").Append(entryPointBase);
            sb.Append("\", EntryPoint = \"").Append(fn.Name).Append("\"), WasmImportLinkage]\n");
            sb.Append("            internal static extern ");
            sb.Append(EmitStubReturnType(fn.Type));
            sb.Append(" wasmImport").Append(methodName).Append('(');
            EmitStubParams(sb, fn.Type);
            sb.Append(");\n");
            sb.Append("\n");  // blank line before closing
            sb.Append("        }\n");
            sb.Append("\n");

            // User-facing wrapper. Note the two-space "public  static
            // unsafe" — verified against the reference output; keep
            // it verbatim.
            sb.Append("        public  static unsafe ");
            sb.Append(FunctionEmit.EmitReturnType(fn.Type));
            sb.Append(' ').Append(methodName).Append('(');
            EmitWrapperParams(sb, fn.Type);
            sb.Append(")\n");
            sb.Append("        {\n");

            var retType = FunctionEmit.EmitReturnType(fn.Type);
            var isVoid = retType == "void";

            // Stub call — note the double-space after `=` in
            // "var result =  {call}" in the wit-bindgen reference.
            sb.Append("            ");
            if (!isVoid) sb.Append("var result =  ");
            sb.Append(stubClassName).Append(".wasmImport").Append(methodName);
            sb.Append('(');
            EmitLoweredArgs(sb, fn.Type);
            sb.Append(");\n");

            if (!isVoid)
            {
                sb.Append("            return ");
                sb.Append(EmitLift(fn.Type.Result!, "result"));
                sb.Append(";\n");
            }

            sb.Append("\n");
            sb.Append("            //TODO: free alloc handle (interopString) if exists\n");
            sb.Append("        }\n");
            sb.Append("\n");
        }

        // ---- Stub type mapping (core wasm ABI) -----------------------------

        /// <summary>
        /// Stub-side core-wasm-ABI type for a parameter. i32 for
        /// bool/all 8/16/32-bit ints/char, i64 for 64-bit ints,
        /// f32/f64 as-is.
        /// </summary>
        private static string StubTypeOf(CtValType t)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    CtPrim.F32 => "float",
                    CtPrim.F64 => "double",
                    CtPrim.S64 => "long",
                    CtPrim.U64 => "long",
                    _ => "int",
                };
            }
            throw new NotImplementedException(
                "Interop stub type for " + t.GetType().Name +
                " is a Phase 1a.2 follow-up.");
        }

        private static string EmitStubReturnType(CtFunctionType sig)
        {
            if (sig.HasNoResult) return "void";
            if (sig.Result == null) return "void";
            return StubTypeOf(sig.Result);
        }

        private static void EmitStubParams(StringBuilder sb, CtFunctionType sig)
        {
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(StubTypeOf(sig.Params[i].Type));
                sb.Append(" p").Append(i);  // positional names p0, p1, ...
            }
        }

        private static void EmitWrapperParams(StringBuilder sb, CtFunctionType sig)
        {
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TypeRefEmit.EmitParam(sig.Params[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(sig.Params[i].Name));
            }
        }

        private static void EmitLoweredArgs(StringBuilder sb, CtFunctionType sig)
        {
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var argName = NameConventions.ToCamelCase(sig.Params[i].Name);
                sb.Append(EmitLower(sig.Params[i].Type, argName));
            }
        }

        // ---- Lift / lower expressions --------------------------------------

        /// <summary>
        /// Build the expression that converts a C# wrapper-side
        /// argument into the stub's core-wasm ABI type. Mirrors the
        /// casts wit-bindgen emits; verified against reference.
        /// </summary>
        private static string EmitLower(CtValType t, string argName)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    // bool → int: ternary.
                    CtPrim.Bool => $"({argName} ? 1 : 0)",
                    // Implicit widening cases — stub takes int; C#
                    // converts these without a cast.
                    CtPrim.S8 or CtPrim.U8 or CtPrim.S16 or CtPrim.U16
                        or CtPrim.S32 or CtPrim.S64 or CtPrim.F32
                        or CtPrim.F64 => argName,
                    // Unsigned 32/64-bit: need `unchecked` cast so
                    // high-bit values don't throw at checked-context
                    // call sites.
                    CtPrim.U32 => $"unchecked((int)({argName}))",
                    CtPrim.U64 => $"unchecked((long)({argName}))",
                    // char is C# uint; wit-bindgen emits a plain cast
                    // rather than unchecked (asymmetric to u32;
                    // verified against the reference).
                    CtPrim.Char => $"((int){argName})",
                    _ => throw new NotImplementedException(
                        "Lower expression for " + p.Kind + " is a follow-up."),
                };
            }
            throw new NotImplementedException(
                "Lower expression for " + t.GetType().Name + " is a follow-up.");
        }

        /// <summary>
        /// Build the expression that converts the stub's return
        /// value into the C# wrapper-side return type.
        /// </summary>
        private static string EmitLift(CtValType t, string resultName)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    CtPrim.Bool => $"({resultName} != 0)",
                    // Narrowing casts from int. Plain `(type)result`.
                    CtPrim.S8 => $"(({NameCs(p.Kind)}){resultName})",
                    CtPrim.U8 => $"(({NameCs(p.Kind)}){resultName})",
                    CtPrim.S16 => $"(({NameCs(p.Kind)}){resultName})",
                    CtPrim.U16 => $"(({NameCs(p.Kind)}){resultName})",
                    CtPrim.S32 => resultName,
                    CtPrim.S64 => resultName,
                    CtPrim.F32 => resultName,
                    CtPrim.F64 => resultName,
                    // Unsigned 32/64-bit: unchecked cast.
                    CtPrim.U32 => $"unchecked(({NameCs(p.Kind)})({resultName}))",
                    CtPrim.U64 => $"unchecked(({NameCs(p.Kind)})({resultName}))",
                    // char is C# uint; same treatment as u32.
                    CtPrim.Char => $"unchecked((uint)({resultName}))",
                    _ => throw new NotImplementedException(
                        "Lift expression for " + p.Kind + " is a follow-up."),
                };
            }
            throw new NotImplementedException(
                "Lift expression for " + t.GetType().Name + " is a follow-up.");
        }

        private static string NameCs(CtPrim kind) => TypeRefEmit.EmitPrim(kind);
    }
}
