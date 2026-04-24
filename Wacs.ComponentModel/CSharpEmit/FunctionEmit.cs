// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Emits C# function / method signatures from
    /// <see cref="CtFunctionType"/>. The output shape exactly matches
    /// wit-bindgen-csharp 0.30.0 — verified against a
    /// function-per-primitive synthetic WIT.
    ///
    /// <para><b>Phase 1a.2 scope:</b> simple export-interface
    /// <c>static abstract</c> declarations with void or primitive
    /// parameters / return. Function bodies (Interop trampolines,
    /// method implementations) are separate emitters shipped in
    /// follow-up commits.</para>
    /// </summary>
    internal static class FunctionEmit
    {
        /// <summary>
        /// Emit a single <c>static abstract</c> method declaration —
        /// the form used inside <c>public interface I{Name} { … }</c>
        /// export-side files.
        ///
        /// <para>Output format:
        /// <c>    static abstract {RET} {MethodName}({TYPE} {argName}, …);</c></para>
        /// </summary>
        public static string EmitStaticAbstractSignature(string wasmFunctionName,
                                                         CtFunctionType sig)
        {
            var sb = new StringBuilder();
            if (EmitAmbient.IncludeWitMetadata
                && NameConventions.ToPascalCase(wasmFunctionName)
                   != wasmFunctionName)
            {
                sb.Append("    [global::Wacs.ComponentModel.WitName(\"")
                  .Append(wasmFunctionName).Append("\")]\n");
            }
            sb.Append("    static abstract ");
            sb.Append(EmitReturnType(sig));
            sb.Append(' ');
            sb.Append(NameConventions.ToPascalCase(wasmFunctionName));
            sb.Append('(');
            EmitParamList(sb, sig);
            sb.Append(");");
            return sb.ToString();
        }

        /// <summary>
        /// Convert a <see cref="CtFunctionType"/>'s result to a C# return
        /// type. Rules, verified against wit-bindgen-csharp 0.30.0:
        /// <list type="bullet">
        /// <item><description>No result → <c>void</c>.</description></item>
        /// <item><description><c>result</c> with both sides elided
        /// (<c>func() -&gt; result</c>) → <c>void</c> at the interface
        /// level. The result discriminant is materialized in the
        /// Interop trampoline, not the interface signature.</description></item>
        /// <item><description>Single anonymous non-result → that type.</description></item>
        /// <item><description>Named results — <b>not yet</b>; wit-bindgen
        /// emits tuple returns.</description></item>
        /// </list>
        /// </summary>
        public static string EmitReturnType(CtFunctionType sig)
        {
            if (sig.HasNoResult) return "void";
            if (sig.Result != null) return TypeRefEmit.EmitReturn(sig.Result);
            // Named results — not yet supported; defer.
            throw new System.NotImplementedException(
                "Multi-result function emission is a Phase 1a.2 follow-up.");
        }

        private static void EmitParamList(StringBuilder sb, CtFunctionType sig)
        {
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = sig.Params[i];
                sb.Append(TypeRefEmit.Emit(p.Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(p.Name));
            }
        }
    }
}
