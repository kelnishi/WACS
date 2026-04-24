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
            var entryPointBase = EntryPoints.InterfaceBase(iface);

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
            sb.Append("\", EntryPoint = \"")
              .Append(EntryPoints.ImportFreeFunction(fn.Name))
              .Append("\"), WasmImportLinkage]\n");
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

            // Prelude: for each string-typed param, pin a UTF-8
            // GCHandle via InteropString.FromString and bind its
            // (ptr, len) into `{paramName}Ptr` / `{paramName}Len`.
            // Emitted before the stub call.
            EmitWrapperPrelude(sb, fn.Type);

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

        /// <summary>
        /// Emit the wrapper body's "lower prelude" — per-param
        /// setup that precedes the stub call. Current coverage:
        /// <c>string</c> params, which pin a UTF-8 GCHandle and
        /// bind <c>{paramName}Ptr</c> + <c>{paramName}Len</c>
        /// locals. Other aggregate lowerings (list, option,
        /// result, tuple) land as follow-ups here.
        ///
        /// <para><b>Local-variable naming diverges from
        /// wit-bindgen-csharp.</b> Upstream uses a local-allocator
        /// with inconsistent suffix rules (<c>result</c> /
        /// <c>result1</c> vs. <c>interopString</c> /
        /// <c>interopString0</c>); we use simple
        /// <c>{paramName}Ptr</c> / <c>{paramName}Len</c> pairs.
        /// The public API / DllImport signatures match byte-for-
        /// byte; only wrapper-body locals differ, so the roundtrip
        /// invariant with <c>componentize-dotnet</c> is preserved
        /// (it never inspects wrapper locals).</para>
        /// </summary>
        private static void EmitWrapperPrelude(StringBuilder sb,
                                               CtFunctionType sig)
        {
            foreach (var p in sig.Params)
            {
                if (p.Type is CtPrimType prim && prim.Kind == CtPrim.String)
                {
                    var argName = NameConventions.ToCamelCase(p.Name);
                    sb.Append("            IntPtr ").Append(argName).Append("Ptr = ");
                    sb.Append("InteropString.FromString(").Append(argName);
                    sb.Append(", out int ").Append(argName).Append("Len);\n");
                }
            }
        }

        // ---- Stub type mapping (core wasm ABI) -----------------------------

        /// <summary>
        /// Stub-side core-wasm-ABI type(s) for one wrapper param.
        /// Most primitives lower to a single i32/i64/f32/f64. Strings
        /// (and, in follow-ups, lists) lower to two stub params:
        /// <c>nint ptr, int len</c>.
        /// </summary>
        private static string[] StubTypesFor(CtValType t)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    CtPrim.String => new[] { "nint", "int" },
                    CtPrim.F32 => new[] { "float" },
                    CtPrim.F64 => new[] { "double" },
                    CtPrim.S64 => new[] { "long" },
                    CtPrim.U64 => new[] { "long" },
                    _ => new[] { "int" },
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
            // Phase 1a.2: string / list / option / result / tuple
            // returns require a return-area pointer param on the
            // stub — not yet implemented; gate keeps them out.
            var types = StubTypesFor(sig.Result);
            if (types.Length != 1)
                throw new NotImplementedException(
                    "Multi-word return lowering is a follow-up.");
            return types[0];
        }

        private static void EmitStubParams(StringBuilder sb, CtFunctionType sig)
        {
            int stubIdx = 0;
            foreach (var p in sig.Params)
            {
                foreach (var stubType in StubTypesFor(p.Type))
                {
                    if (stubIdx > 0) sb.Append(", ");
                    sb.Append(stubType).Append(" p").Append(stubIdx);
                    stubIdx++;
                }
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
            bool first = true;
            foreach (var p in sig.Params)
            {
                var argName = NameConventions.ToCamelCase(p.Name);
                if (p.Type is CtPrimType prim && prim.Kind == CtPrim.String)
                {
                    // String lowered by the prelude to (ptr, len).
                    if (!first) sb.Append(", ");
                    sb.Append(argName).Append("Ptr.ToInt32(), ").Append(argName).Append("Len");
                    first = false;
                }
                else
                {
                    if (!first) sb.Append(", ");
                    sb.Append(EmitLower(p.Type, argName));
                    first = false;
                }
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
