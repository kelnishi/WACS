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
            var usesReturnArea = UsesReturnArea(fn.Type);
            var hasPrelude = HasPrelude(fn.Type);

            // wit-bindgen inserts a blank line between the opening
            // `{` and the body when the body has either a prelude
            // OR a return area.
            if (hasPrelude || usesReturnArea) sb.Append("\n");

            // Prelude: for each string-typed param, pin a UTF-8
            // GCHandle via InteropString.FromString and bind its
            // (ptr, len) into `{paramName}Ptr` / `{paramName}Len`.
            // list<u8> params stackalloc a buffer + CopyTo. Emitted
            // before the stub call.
            EmitWrapperPrelude(sb, fn.Type);
            if (usesReturnArea)
            {
                EmitReturnAreaWrapperBody(sb, stubClassName, methodName, fn.Type);
            }
            else
            {
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
            }

            sb.Append("\n");
            sb.Append("            //TODO: free alloc handle (interopString) if exists\n");
            sb.Append("        }\n");
            sb.Append("\n");
        }

        /// <summary>
        /// Emit the wrapper body when the function lowers its
        /// return through a return-area buffer. Allocates a
        /// stack-pinned <c>uint[N]</c>, passes its address as the
        /// trailing <c>nint</c> stub param, then lifts the
        /// return-typed value from the buffer.
        /// </summary>
        private static void EmitReturnAreaWrapperBody(StringBuilder sb,
                                                       string stubClassName,
                                                       string methodName,
                                                       CtFunctionType sig)
        {
            var words = ReturnAreaUintCount(sig);
            sb.Append("            var retArea = new uint[").Append(words).Append("];\n");
            sb.Append("            fixed (uint* retAreaByte0 = &retArea[0])\n");
            sb.Append("            {\n");
            sb.Append("                var ptr = (nint)retAreaByte0;\n");
            sb.Append("                ").Append(stubClassName).Append(".wasmImport").Append(methodName);
            sb.Append('(');
            EmitLoweredArgs(sb, sig);
            if (sig.Params.Count > 0) sb.Append(", ");
            sb.Append("ptr");
            sb.Append(");\n");

            if (IsListOfPrim(sig.Result!))
            {
                // list<prim> return — allocate managed array of the
                // reported length, then Span-copy the wasm memory
                // into it. Length is always read via
                // BitConverter.ToInt32 (the length word is u32
                // regardless of element type); the Span<T> element
                // type matches the primitive's C# name.
                // Blank separator preceding matches wit-bindgen.
                var elemKind = ((CtPrimType)((CtListType)sig.Result!).Element).Kind;
                var elemCs = TypeRefEmit.EmitPrim(elemKind);
                sb.Append("\n");
                sb.Append("                var array = new ").Append(elemCs)
                  .Append("[BitConverter.ToInt32(new Span<byte>((void*)(ptr + 4), 4))];\n");
                sb.Append("                new Span<").Append(elemCs)
                  .Append(">((void*)(BitConverter.ToInt32(new Span<byte>((void*)(ptr + 0), 4))), BitConverter.ToInt32(new Span<byte>((void*)(ptr + 4), 4))).CopyTo(new Span<")
                  .Append(elemCs).Append(">(array));\n");
                sb.Append("                return array;\n");
            }
            else if (IsTupleOfSmallPrims(sig.Result!))
            {
                // tuple<P1, P2, …> return — lift each element
                // inline at its 4-byte offset. The closing `)`
                // lands on its own line — preserved from the
                // wit-bindgen 0.30.0 reference formatting quirk.
                var tup = (CtTupleType)sig.Result!;
                sb.Append("                return (");
                for (int i = 0; i < tup.Elements.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var kind = ((CtPrimType)tup.Elements[i]).Kind;
                    sb.Append(EmitReturnAreaPrimLift(kind, offset: i * 4));
                }
                sb.Append('\n');
                sb.Append("                );\n");
            }
            else if (IsOptionOfSmallPrim(sig.Result!))
            {
                // option<prim> return — discriminant at offset 0
                // (1 byte), payload at offset 4 (1 word). Switch
                // on the discriminant, lift the payload on the
                // Some branch, bind null on the None branch. The
                // blank line between `{` and the declaration
                // matches wit-bindgen's reference output.
                sb.Append("\n");
                var opt = (CtOptionType)sig.Result!;
                var innerKind = ((CtPrimType)opt.Inner).Kind;
                var cs = TypeRefEmit.EmitPrim(innerKind);
                sb.Append("                ").Append(cs).Append("? lifted;\n");
                sb.Append("\n");
                sb.Append("                switch (new Span<byte>((void*)(ptr + 0), 1)[0]) {\n");
                sb.Append("                    case 0: {\n");
                sb.Append("                        lifted = null;\n");
                sb.Append("                        break;\n");
                sb.Append("                    }\n");
                sb.Append("\n");
                sb.Append("                    case 1: {\n");
                sb.Append("\n");
                sb.Append("                        lifted = ");
                sb.Append(EmitReturnAreaPrimLift(innerKind, offset: 4));
                sb.Append(";\n");
                sb.Append("                        break;\n");
                sb.Append("                    }\n");
                sb.Append("\n");
                sb.Append("                    default: throw new ArgumentException(\"invalid discriminant: \" + (new Span<byte>((void*)(ptr + 0), 1)[0]));\n");
                sb.Append("                }\n");
                sb.Append("                return lifted;\n");
            }
            else
            {
                sb.Append("                return ");
                sb.Append(EmitReturnAreaLift(sig.Result!));
                sb.Append(";\n");
            }
            sb.Append("            }\n");
        }

        /// <summary>
        /// Build the expression that reads a single-word primitive
        /// value out of the return area at byte <paramref name="offset"/>.
        /// Used by <c>option&lt;prim&gt;</c> lifting — the
        /// discriminant branch has to read the payload at a fixed
        /// (+4) offset. Values narrower than 32-bit (<c>bool</c>,
        /// <c>s8..u16</c>) are read as an int and cast; 32-bit
        /// primitives go through BitConverter.
        /// </summary>
        private static string EmitReturnAreaPrimLift(CtPrim kind, int offset)
        {
            var off = "ptr + " + offset;
            return kind switch
            {
                CtPrim.S32 =>
                    $"BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))",
                CtPrim.U32 =>
                    $"unchecked((uint)(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))))",
                CtPrim.F32 =>
                    $"BitConverter.ToSingle(new Span<byte>((void*)({off}), 4))",
                CtPrim.Bool =>
                    $"(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4)) != 0)",
                // Narrower-than-32-bit primitives — wit-bindgen reads
                // the full 4-byte slot then narrows. Cast expressions
                // mirror EmitLift for direct-return primitives.
                CtPrim.S8 =>
                    $"((sbyte)(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))))",
                CtPrim.U8 =>
                    $"((byte)(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))))",
                CtPrim.S16 =>
                    $"((short)(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))))",
                CtPrim.U16 =>
                    $"((ushort)(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))))",
                CtPrim.Char =>
                    $"unchecked((uint)(BitConverter.ToInt32(new Span<byte>((void*)({off}), 4))))",
                _ => throw new NotImplementedException(
                    "Return-area lift for " + kind + " is a follow-up."),
            };
        }

        /// <summary>
        /// Read a return-typed value out of the return area at
        /// local <c>ptr</c>. Currently handles <c>string</c> —
        /// reads (ptr, len) at offsets 0 and 4 and decodes UTF-8.
        /// Follow-ups: list, record, option, result, tuple.
        /// </summary>
        private static string EmitReturnAreaLift(CtValType t)
        {
            if (t is CtPrimType p && p.Kind == CtPrim.String)
            {
                return "Encoding.UTF8.GetString("
                    + "(byte*)BitConverter.ToInt32(new Span<byte>((void*)(ptr + 0), 4)), "
                    + "BitConverter.ToInt32(new Span<byte>((void*)(ptr + 4), 4)))";
            }
            if (IsByteList(t))
            {
                // byte[] — need multi-statement body, not a single
                // expression. Caller must handle via EmitReturnAreaLiftStmts.
                throw new System.InvalidOperationException(
                    "byte[] lift requires multi-statement emission; " +
                    "use EmitReturnAreaWrapperBody's byte-list branch.");
            }
            throw new NotImplementedException(
                "Return-area lift for " + t.GetType().Name +
                " is a follow-up.");
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
        /// <summary>
        /// True if any param in the signature needs a
        /// marshaling prelude (string or list&lt;u8&gt;).
        /// Controls whether a blank line appears after the wrapper's
        /// opening <c>{</c> — wit-bindgen emits one iff the body
        /// has a prelude.
        /// </summary>
        private static bool HasPrelude(CtFunctionType sig)
        {
            foreach (var p in sig.Params)
            {
                if (p.Type is CtPrimType prim && prim.Kind == CtPrim.String)
                    return true;
                if (IsListOfPrim(p.Type)) return true;
                if (IsOptionOfSmallPrim(p.Type)) return true;
            }
            return false;
        }

        private static void EmitWrapperPrelude(StringBuilder sb,
                                               CtFunctionType sig)
        {
            // Index across list<u8> params — wit-bindgen names them
            // `buffer`, `buffer1`, `buffer2`, … regardless of the
            // arg name (string params use their own arg-derived
            // naming).
            int listIdx = 0;
            foreach (var p in sig.Params)
            {
                var argName = NameConventions.ToCamelCase(p.Name);
                if (p.Type is CtPrimType prim && prim.Kind == CtPrim.String)
                {
                    sb.Append("            IntPtr ").Append(argName).Append("Ptr = ");
                    sb.Append("InteropString.FromString(").Append(argName);
                    sb.Append(", out int ").Append(argName).Append("Len);\n");
                }
                else if (IsListOfPrim(p.Type))
                {
                    // Stack-allocate a buffer sized to the managed
                    // array, copy elements in, pass buffer pointer +
                    // length into the stub. Buffer name follows
                    // wit-bindgen: `buffer` (unsuffixed) for the
                    // first list<prim> param, `buffer1`/`buffer2`/…
                    // for subsequent ones. The C# element type
                    // (byte/uint/ulong/…) comes from the WIT
                    // primitive via TypeRefEmit.EmitPrim.
                    var elemKind = ((CtPrimType)((CtListType)p.Type).Element).Kind;
                    var elemCs = TypeRefEmit.EmitPrim(elemKind);
                    var bufName = listIdx == 0 ? "buffer" : ("buffer" + listIdx);
                    sb.Append("            void* ").Append(bufName).Append(" = ");
                    sb.Append("stackalloc ").Append(elemCs)
                      .Append("[(").Append(argName).Append(").Length];\n");
                    sb.Append("            ").Append(argName);
                    sb.Append(".AsSpan<").Append(elemCs).Append(">().CopyTo(new Span<")
                      .Append(elemCs).Append(">(")
                      .Append(bufName).Append(", ").Append(argName).Append(".Length));\n");
                    listIdx++;
                }
                else if (IsOptionOfSmallPrim(p.Type))
                {
                    // option<prim> lowers to (discriminant, payload)
                    // pair. Emit per-param `{arg}Tag` (int disc) and
                    // `{arg}Val` (inner stub type) locals and
                    // populate them from a nullable-check branch.
                    // Local-name divergence from wit-bindgen (they
                    // allocate shared-counter names like `lowered` /
                    // `lowered3`); our per-arg naming is clearer and
                    // doesn't affect the DllImport signature —
                    // the componentize-dotnet roundtrip never
                    // inspects wrapper locals.
                    var opt = (CtOptionType)p.Type;
                    var innerPrim = (CtPrimType)opt.Inner;
                    var innerStub = StubTypesFor(opt.Inner)[0];
                    sb.Append("            int ").Append(argName).Append("Tag;\n");
                    sb.Append("            ").Append(innerStub).Append(' ')
                      .Append(argName).Append("Val;\n");
                    sb.Append("            if (").Append(argName).Append(" != null) {\n");
                    sb.Append("                ").Append(argName).Append("Tag = 1;\n");
                    sb.Append("                ").Append(argName).Append("Val = ");
                    sb.Append(EmitOptionPayloadLower(innerPrim.Kind, argName));
                    sb.Append(";\n");
                    sb.Append("            } else {\n");
                    sb.Append("                ").Append(argName).Append("Tag = 0;\n");
                    sb.Append("                ").Append(argName).Append("Val = ");
                    sb.Append(OptionPayloadZero(innerStub));
                    sb.Append(";\n");
                    sb.Append("            }\n");
                }
            }
        }

        /// <summary>
        /// Emit the payload-lowering expression for an
        /// <c>option&lt;prim&gt;</c> param with non-null payload.
        /// The outer null-check has already unwrapped to the
        /// non-null arm; we cast through <c>(prim)argName</c> (to
        /// force the <c>Nullable&lt;T&gt;.Value</c> unbox) then into
        /// the stub's core-wasm type.
        /// </summary>
        private static string EmitOptionPayloadLower(CtPrim kind, string argName)
        {
            // Unwrap Nullable<T> to the primitive then reuse the
            // primitive-lowering logic. The cast syntax `(uint)x`
            // on a `uint?` is the idiomatic unwrap — equivalent to
            // `x.Value` but matches wit-bindgen's form.
            var unwrapped = "(" + TypeRefEmit.EmitPrim(kind) + ")" + argName;
            return EmitLower(new CtPrimType(kind), unwrapped);
        }

        /// <summary>
        /// Zero value of the stub payload type for the null branch.
        /// Integer stubs take <c>0</c>; f32 stubs take <c>0f</c>.
        /// </summary>
        private static string OptionPayloadZero(string stubType) =>
            stubType == "float" ? "0f" : "0";

        // ---- Stub type mapping (core wasm ABI) -----------------------------

        /// <summary>
        /// Stub-side core-wasm-ABI type(s) for one wrapper param.
        /// Most primitives lower to a single i32/i64/f32/f64. Strings
        /// and <c>list&lt;u8&gt;</c> lower to two stub params:
        /// <c>nint ptr, int len</c>. Other aggregates are follow-ups.
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
            if (IsListOfPrim(t))
            {
                // list<prim> lowers to (ptr, len) like string but
                // without encoding conversion.
                return new[] { "nint", "int" };
            }
            if (IsOptionOfSmallPrim(t))
            {
                // option<P> lowers to (discriminant, payload). The
                // discriminant is always an i32; the payload slot
                // takes the inner primitive's stub type (e.g. float
                // for option<f32>).
                var inner = ((CtOptionType)t).Inner;
                var innerStub = StubTypesFor(inner);
                return new[] { "int", innerStub[0] };
            }
            if (IsTupleOfSmallPrims(t))
            {
                // tuple<P1, P2, …> flattens to the concatenation of
                // each element's stub types. Every element is small-
                // prim so each contributes exactly one word.
                var tup = (CtTupleType)t;
                var flat = new string[tup.Elements.Count];
                for (int i = 0; i < tup.Elements.Count; i++)
                    flat[i] = StubTypesFor(tup.Elements[i])[0];
                return flat;
            }
            throw new NotImplementedException(
                "Interop stub type for " + t.GetType().Name +
                " is a Phase 1a.2 follow-up.");
        }

        /// <summary>
        /// True when <paramref name="t"/> is <c>list&lt;prim&gt;</c>
        /// for any numeric primitive (bool / s8 / u8 / s16 / u16 /
        /// s32 / u32 / s64 / u64 / f32 / f64 / char). All lower via
        /// the same <c>stackalloc</c> + <c>AsSpan().CopyTo</c>
        /// pattern that wit-bindgen emits — the only difference is
        /// the element type name. <c>list&lt;string&gt;</c> and
        /// <c>list&lt;aggregate&gt;</c> need element-wise marshaling
        /// and are a follow-up.
        /// </summary>
        internal static bool IsListOfPrim(CtValType t) =>
            t is CtListType l
            && l.Element is CtPrimType pe
            && pe.Kind != CtPrim.String;

        /// <summary>Back-compat alias; byte lists are the historical
        /// first-supported case. Now handled identically to any
        /// other <see cref="IsListOfPrim"/> list.</summary>
        internal static bool IsByteList(CtValType t) =>
            t is CtListType l
            && l.Element is CtPrimType pe
            && pe.Kind == CtPrim.U8;

        /// <summary>
        /// True when <paramref name="t"/> is <c>option&lt;P&gt;</c>
        /// where <c>P</c> is a primitive that fits in one
        /// core-wasm word (i32 / u32 / bool / char / f32 / s8 /
        /// u8 / s16 / u16). 64-bit payloads and aggregate payloads
        /// are a follow-up: the return area needs a 3-word layout
        /// (discriminant + 2-word payload with 8-byte alignment) and
        /// the lowering/lifting expressions differ.
        /// </summary>
        internal static bool IsOptionOfSmallPrim(CtValType t) =>
            t is CtOptionType o
            && o.Inner is CtPrimType op
            && IsSmallPrim(op.Kind);

        /// <summary>
        /// True when <paramref name="t"/> is a <c>tuple</c> whose
        /// every element is a small primitive (≤ 1 core-wasm word).
        /// Such tuples flatten directly into the stub signature — no
        /// alignment-padding concerns — and lift from the return
        /// area as a flat sequence at offsets 0, 4, 8, … . Tuples
        /// with 64-bit, aggregate, or resource-valued elements need
        /// alignment + stride-aware emission (a follow-up).
        /// </summary>
        internal static bool IsTupleOfSmallPrims(CtValType t)
        {
            if (!(t is CtTupleType tup)) return false;
            if (tup.Elements.Count == 0) return false;
            foreach (var e in tup.Elements)
            {
                if (!(e is CtPrimType p)) return false;
                if (!IsSmallPrim(p.Kind)) return false;
            }
            return true;
        }

        private static bool IsSmallPrim(CtPrim k) => k switch
        {
            CtPrim.Bool or CtPrim.S8 or CtPrim.U8 or CtPrim.S16
                or CtPrim.U16 or CtPrim.S32 or CtPrim.U32
                or CtPrim.F32 or CtPrim.Char => true,
            _ => false,
        };

        private static string EmitStubReturnType(CtFunctionType sig)
        {
            if (sig.HasNoResult) return "void";
            if (sig.Result == null) return "void";
            // Returns that lower to multiple words (string, list,
            // record, option, result with payload) get a return-area
            // pointer param on the stub instead — the stub's C#
            // return becomes void.
            if (UsesReturnArea(sig)) return "void";
            var types = StubTypesFor(sig.Result);
            if (types.Length != 1)
                throw new NotImplementedException(
                    "Multi-word non-return-area return lowering is a follow-up.");
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
            // Trailing return-area pointer if the return is a
            // multi-word aggregate.
            if (UsesReturnArea(sig))
            {
                if (stubIdx > 0) sb.Append(", ");
                sb.Append("nint p").Append(stubIdx);
            }
        }

        /// <summary>
        /// True when the function return needs a return-area
        /// pointer param on the stub. Currently only <c>string</c>
        /// (ptr + len, 2 u32s). Follow-up aggregates (list, record,
        /// option, result, tuple) slot in here with their own
        /// return-area sizes.
        /// </summary>
        internal static bool UsesReturnArea(CtFunctionType sig)
        {
            if (sig.HasNoResult || sig.Result == null) return false;
            if (sig.Result is CtPrimType p && p.Kind == CtPrim.String) return true;
            if (IsListOfPrim(sig.Result)) return true;
            if (IsOptionOfSmallPrim(sig.Result)) return true;
            if (IsTupleOfSmallPrims(sig.Result)) return true;
            return false;
        }

        /// <summary>
        /// Return-area byte size for a function's return type, in
        /// multiples of 4 bytes (uints). <c>string</c> and
        /// <c>list&lt;u8&gt;</c> = 2 (ptr + len). Other aggregates
        /// plug in as they're supported.
        /// </summary>
        private static int ReturnAreaUintCount(CtFunctionType sig)
        {
            if (sig.Result is CtPrimType p && p.Kind == CtPrim.String) return 2;
            if (IsListOfPrim(sig.Result!)) return 2;
            // option<small-prim>: discriminant word at offset 0,
            // payload word at offset 4 — 2 uints total.
            if (IsOptionOfSmallPrim(sig.Result!)) return 2;
            // tuple<small-prim, small-prim, …>: one word per element,
            // laid out at offsets 0, 4, 8, … .
            if (IsTupleOfSmallPrims(sig.Result!))
                return ((CtTupleType)sig.Result!).Elements.Count;
            throw new NotImplementedException(
                "Return area sizing for " + sig.Result?.GetType().Name +
                " is a follow-up.");
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
            int listIdx = 0;
            foreach (var p in sig.Params)
            {
                var argName = NameConventions.ToCamelCase(p.Name);
                if (!first) sb.Append(", ");
                first = false;
                if (p.Type is CtPrimType prim && prim.Kind == CtPrim.String)
                {
                    // String lowered by the prelude to (ptr, len).
                    sb.Append(argName).Append("Ptr.ToInt32(), ").Append(argName).Append("Len");
                }
                else if (IsListOfPrim(p.Type))
                {
                    // list<prim> lowered to (buffer-ptr, length).
                    // Length expression has extra parens —
                    // wit-bindgen emits `(data).Length` here but
                    // `data.Length` inside the CopyTo call;
                    // preserve the asymmetry.
                    var bufName = listIdx == 0 ? "buffer" : ("buffer" + listIdx);
                    sb.Append("(int)").Append(bufName).Append(", ");
                    sb.Append('(').Append(argName).Append(").Length");
                    listIdx++;
                }
                else if (IsOptionOfSmallPrim(p.Type))
                {
                    // option<prim> lowered by the prelude to
                    // `{arg}Tag, {arg}Val`.
                    sb.Append(argName).Append("Tag, ").Append(argName).Append("Val");
                }
                else if (IsTupleOfSmallPrims(p.Type))
                {
                    // tuple<P1, P2, …> lowered inline as
                    // `{Lower(P1, argName.Item1)}, {Lower(P2, argName.Item2)}, …`.
                    // No prelude needed — the C# tuple gives direct
                    // field access on the fly.
                    var tup = (CtTupleType)p.Type;
                    for (int i = 0; i < tup.Elements.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        var elemArg = argName + ".Item" + (i + 1);
                        sb.Append(EmitLower(tup.Elements[i], elemArg));
                    }
                }
                else
                {
                    sb.Append(EmitLower(p.Type, argName));
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
