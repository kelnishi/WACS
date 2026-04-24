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
    /// Emits wit-bindgen-csharp-shaped export-side Interop files —
    /// the <c>{InterfaceName}Interop.cs</c> source containing
    /// <c>[UnmanagedCallersOnly]</c> trampolines (and
    /// <c>cabi_post_*</c> cleanup trampolines for non-void returns)
    /// for every free function the exported interface declares.
    ///
    /// <para>Trampolines bridge the wasm host's
    /// core-ABI-shaped call site into the user's <c>{Iface}Impl</c>
    /// class. Args are lifted from core-wasm types to the C# wrapper
    /// types the Impl expects; the return is lowered back to core
    /// ABI.</para>
    ///
    /// <para><b>Phase 1a.2 scope:</b> primitive-only signatures.
    /// Aggregate / resource-typed trampolines, plus genuine cabi_post
    /// cleanup bodies, are follow-ups — they need canonical-ABI
    /// return-area emission and memory allocator wiring.</para>
    /// </summary>
    internal static class TrampolineEmit
    {
        /// <summary>
        /// Build a full <c>{InterfaceName}Interop.cs</c> source for
        /// an exported interface whose every free function uses only
        /// primitive types. Caller must ensure emittability.
        /// </summary>
        public static string EmitExportTrampolineContent(
            CtInterfaceType iface,
            string worldNameKebab)
        {
            if (iface.Package == null)
                throw new ArgumentException(
                    "Trampoline emission requires a packaged interface.",
                    nameof(iface));

            var ifaceNs = NameConventions.InterfaceNamespace(
                worldNameKebab, isExport: true, iface.Package);
            var className = NameConventions.ToPascalCase(iface.Name) + "Interop";
            var implClassName = NameConventions.ToPascalCase(iface.Name) + "Impl";
            var entryPointBase = BuildEntryPointBase(iface);

            var sb = new StringBuilder();
            sb.Append(CSharpEmitter.Header);
            sb.Append("namespace ").Append(ifaceNs).Append("\n{\n");
            sb.Append("    public static class ").Append(className).Append(" {\n\n");

            foreach (var fn in iface.Functions)
            {
                EmitFreeFunctionTrampoline(sb, fn, entryPointBase, implClassName);
            }

            sb.Append("    }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// <c>{ns}:{path0}:{path1}/{iface-name}@{ver}</c> — the
        /// shared prefix for per-function entry points. Each
        /// function trampoline appends <c>#{fn-name}</c>.
        /// </summary>
        private static string BuildEntryPointBase(CtInterfaceType iface)
        {
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

        private static void EmitFreeFunctionTrampoline(
            StringBuilder sb,
            CtInterfaceFunction fn,
            string entryPointBase,
            string implClassName)
        {
            var methodName = NameConventions.ToPascalCase(fn.Name);
            var entryPoint = entryPointBase + "#" + fn.Name;

            var stubRet = StubReturnType(fn.Type);
            var isVoid = stubRet == "void";

            sb.Append("        [UnmanagedCallersOnly(EntryPoint = \"");
            sb.Append(entryPoint).Append("\")]\n");
            sb.Append("        public static unsafe ");
            sb.Append(stubRet);
            sb.Append(" wasmExport").Append(methodName).Append('(');
            EmitStubParams(sb, fn.Type);
            sb.Append(") {\n");
            sb.Append("\n");  // blank line inside

            if (!isVoid)
            {
                sb.Append("            ");
                sb.Append(TypeRefEmit.EmitReturn(fn.Type.Result!));
                sb.Append(" ret;\n");
                sb.Append("            ret = ").Append(implClassName).Append('.').Append(methodName);
                sb.Append('(');
                EmitLiftedArgs(sb, fn.Type);
                sb.Append(");\n");
                sb.Append("            return ");
                sb.Append(EmitLower(fn.Type.Result!, "ret"));
                sb.Append(";\n");
            }
            else
            {
                sb.Append("            ").Append(implClassName).Append('.').Append(methodName);
                sb.Append('(');
                EmitLiftedArgs(sb, fn.Type);
                sb.Append(");\n");
            }

            sb.Append("\n");  // blank line before closing
            sb.Append("        }\n");
            sb.Append("\n");

            // cabi_post_* cleanup trampoline — only emitted for
            // non-void returns. Phase 1a.2 body is a TODO stub
            // (wit-bindgen 0.30.0 emits exactly this); real
            // cleanup bodies come when the canonical-ABI
            // return-area + dealloc emitters land.
            if (!isVoid)
            {
                sb.Append("        [UnmanagedCallersOnly(EntryPoint = \"cabi_post_");
                sb.Append(entryPoint).Append("\")]\n");
                sb.Append("        public static void cabi_post_wasmExport").Append(methodName);
                sb.Append('(').Append(stubRet).Append(" returnValue) {\n");
                sb.Append("            Console.WriteLine(\"TODO: cabi_post_");
                sb.Append(entryPoint).Append("\");\n");
                sb.Append("        }\n");
                sb.Append("\n");
            }
        }

        // ---- Stub ABI mapping ---------------------------------------------

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
                "Trampoline stub type for " + t.GetType().Name +
                " is a follow-up.");
        }

        private static string StubReturnType(CtFunctionType sig)
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
                sb.Append(" p").Append(i);
            }
        }

        private static void EmitLiftedArgs(StringBuilder sb, CtFunctionType sig)
        {
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('(');
                sb.Append(EmitLiftArg(sig.Params[i].Type, "p" + i));
                sb.Append(')');
            }
        }

        // ---- Lift / lower expressions --------------------------------------

        /// <summary>
        /// Convert the stub-side core-ABI argument to the wrapper
        /// (Impl-side) parameter type. Matches wit-bindgen's quirk
        /// of wrapping the expression in extra parens at the call
        /// site (done by <see cref="EmitLiftedArgs"/>), so the
        /// expression returned here is just the inner form.
        /// </summary>
        private static string EmitLiftArg(CtValType t, string argName)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    CtPrim.Bool => $"({argName} != 0)",
                    CtPrim.S8 => $"((sbyte){argName})",
                    CtPrim.U8 => $"((byte){argName})",
                    CtPrim.S16 => $"((short){argName})",
                    CtPrim.U16 => $"((ushort){argName})",
                    CtPrim.S32 or CtPrim.S64 or CtPrim.F32 or CtPrim.F64 => argName,
                    CtPrim.U32 => $"unchecked((uint)({argName}))",
                    CtPrim.U64 => $"unchecked((ulong)({argName}))",
                    // char (C# uint): trampoline lift uses unchecked
                    // cast — asymmetric with import-side lower which
                    // uses a plain cast.
                    CtPrim.Char => $"unchecked((uint)({argName}))",
                    _ => throw new NotImplementedException(
                        "Lift arg for " + p.Kind + " is a follow-up."),
                };
            }
            throw new NotImplementedException(
                "Lift arg for " + t.GetType().Name + " is a follow-up.");
        }

        /// <summary>
        /// Convert the Impl-side return value to the stub-side
        /// core-ABI return type.
        /// </summary>
        private static string EmitLower(CtValType t, string retName)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    CtPrim.Bool => $"({retName} ? 1 : 0)",
                    // Implicit widening back to int / long / same —
                    // no cast needed.
                    CtPrim.S8 or CtPrim.U8 or CtPrim.S16 or CtPrim.U16
                        or CtPrim.S32 or CtPrim.S64 or CtPrim.F32
                        or CtPrim.F64 => retName,
                    CtPrim.U32 => $"unchecked((int)({retName}))",
                    CtPrim.U64 => $"unchecked((long)({retName}))",
                    // char (C# uint): plain cast on lower (matches
                    // import-side behavior; asymmetric with lift).
                    CtPrim.Char => $"((int){retName})",
                    _ => throw new NotImplementedException(
                        "Lower return for " + p.Kind + " is a follow-up."),
                };
            }
            throw new NotImplementedException(
                "Lower return for " + t.GetType().Name + " is a follow-up.");
        }
    }
}
