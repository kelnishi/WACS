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
            var worldNs = NameConventions.WorldNamespaceName(worldNameKebab);
            var className = NameConventions.ToPascalCase(iface.Name) + "Interop";
            var entryPointBase = EntryPoints.InterfaceBase(iface);

            // Push ambient scope so deep callers can reach the world
            // namespace (needed for `new global::{WorldNs}.None()`
            // inside result-lift arms, and cross-interface type ref
            // qualification). Interop files live in a sibling static
            // class — they have no nested scope for type-name
            // elision, so every named type ref must qualify.
            using var scope = EmitAmbient.Push(worldNs, iface,
                alwaysQualifyTypeRefs: true);

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

            // Resource-ref params get `var handle{N} = arg.Handle;`
            // (+ `arg.Handle = 0;` for own<R>) emitted first, with
            // no blank line before. Comes BEFORE the prelude
            // blank-line rule — wit-bindgen structures the body so
            // handle extraction is flush against the opening brace.
            EmitResourceRefHandles(sb, fn.Type, startingSlot: 0);

            // wit-bindgen inserts a blank line between the opening
            // `{` (or after the handle-extraction block) and the
            // body when the body has either a prelude OR a return
            // area.
            if (hasPrelude || usesReturnArea) sb.Append("\n");

            // Prelude: for each string-typed param, pin a UTF-8
            // GCHandle via InteropString.FromString and bind its
            // (ptr, len) into `{paramName}Ptr` / `{paramName}Len`.
            // list<u8> params stackalloc a buffer + CopyTo. Emitted
            // before the stub call.
            EmitWrapperPrelude(sb, fn.Type);
            // When the prelude and return-area coexist, wit-bindgen
            // separates them with a blank line. Preludes without a
            // retArea don't need this (their output is flush against
            // the stub call that follows); retArea-only cases
            // already got their blank from the after-`{` rule.
            if (hasPrelude && usesReturnArea) sb.Append("\n");
            if (usesReturnArea)
            {
                EmitReturnAreaWrapperBody(sb, stubClassName, methodName, fn.Type);
            }
            else if (IsElidedResultType(fn.Type.Result))
            {
                // result<_, _> — stub returns an i32 discriminant;
                // wrapper captures it, builds a Result<None, None>
                // via the same switch + throw-on-err shape as the
                // return-area result path, but with `switch (result)`
                // instead of a Span-byte read.
                EmitElidedResultWrapperBody(sb, stubClassName, methodName, fn.Type);
            }
            else if (fn.Type.Result != null && IsOwnedResource(fn.Type.Result))
            {
                // own<R> return — stub returns i32 handle; wrapper
                // builds a resource wrapper via
                // `new R(new R.THandle(handle))` and returns it.
                sb.Append("            var result =  ");
                sb.Append(stubClassName).Append(".wasmImport").Append(methodName);
                sb.Append('(');
                EmitLoweredArgs(sb, fn.Type);
                sb.Append(");\n");
                var target = ResolveOwnedResource(fn.Type.Result);
                var qualified = TypeRefEmit.EmitQualifiedPath(target);
                sb.Append("            var resource = new ").Append(qualified);
                sb.Append("(new ").Append(qualified).Append(".THandle(result));\n");
                sb.Append("            return resource;\n");
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
        internal static void EmitReturnAreaWrapperBody(StringBuilder sb,
                                                        string stubClassName,
                                                        string methodName,
                                                        CtFunctionType sig,
                                                        string? leadingStubArg = null,
                                                        int resHandleStartingSlot = 0)
        {
            var ra = GetRetAreaInfo(sig);
            sb.Append("            var retArea = new ").Append(ra.Backing)
              .Append('[').Append(ra.Count).Append("];\n");
            sb.Append("            fixed (").Append(ra.Backing)
              .Append("* retAreaByte0 = &retArea[0])\n");
            sb.Append("            {\n");
            sb.Append("                var ptr = (nint)retAreaByte0;\n");
            sb.Append("                ").Append(stubClassName).Append(".wasmImport").Append(methodName);
            sb.Append('(');
            if (leadingStubArg != null)
            {
                sb.Append(leadingStubArg);
                if (sig.Params.Count > 0) sb.Append(", ");
            }
            EmitLoweredArgs(sb, sig, resHandleStartingSlot);
            if (sig.Params.Count > 0 || leadingStubArg != null) sb.Append(", ");
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
                EmitPinnedHandleCleanup(sb, sig, "                ");
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
            else if (IsRecordOfSmallPrims(sig.Result!))
            {
                // record return — build via `new GlobalQualified (
                //   arg0, arg1, ...);` — the record constructor
                // takes positional field args. wit-bindgen quirks:
                // (a) space between the type name and `(`; (b) the
                // args land on a new line at 16-space indent; (c)
                // the closing `);` is on the same line as the last
                // arg.
                var target = ResolveRecord((CtTypeRef)sig.Result!);
                var rec = (CtRecordType)target.Type;
                var qualified = TypeRefEmit.EmitQualifiedPath(target);
                sb.Append("                return new ").Append(qualified).Append(" (\n");
                sb.Append("                ");
                for (int i = 0; i < rec.Fields.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var kind = ((CtPrimType)rec.Fields[i].Type).Kind;
                    sb.Append(EmitReturnAreaPrimLift(kind, offset: i * 4));
                }
                sb.Append(");\n");
            }
            else if (IsResultOfPrimOrNone(sig.Result!))
            {
                EmitResultReturnAreaLift(sb, (CtResultType)sig.Result!, ra.PayloadOffset);
            }
            else if (IsVariantOfSmallPrimOrNone(sig.Result!))
            {
                // Blank line between the stub call and the lift
                // declaration matches wit-bindgen's formatting.
                sb.Append("\n");
                EmitVariantReturnAreaLift(sb, (CtTypeRef)sig.Result!,
                                          discOffset: 0,
                                          payloadOffset: ra.PayloadOffset,
                                          varName: "lifted",
                                          indent: "                ");
                sb.Append("                return lifted;\n");
            }
            else if (IsOptionOfSmallPrim(sig.Result!))
            {
                // option<prim> return — discriminant at offset 0
                // (1 byte), payload at the inner prim's natural
                // alignment: 4 for 32-bit, 8 for 64-bit. Switch on
                // the discriminant, lift payload on Some, null on
                // None. Blank line between `{` and declaration
                // matches wit-bindgen formatting.
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
                sb.Append(EmitReturnAreaPrimLift(innerKind, ra.PayloadOffset));
                sb.Append(";\n");
                sb.Append("                        break;\n");
                sb.Append("                    }\n");
                sb.Append("\n");
                sb.Append("                    default: throw new ArgumentException(\"invalid discriminant: \" + (new Span<byte>((void*)(ptr + 0), 1)[0]));\n");
                sb.Append("                }\n");
                sb.Append("                return lifted;\n");
            }
            else if (IsOptionOfString(sig.Result!))
            {
                // option<string> return — disc at offset 0,
                // payload (ptr, len) at offsets 4 and 8. Same
                // switch shape as option<prim>; Some arm lifts via
                // Encoding.UTF8.GetString(ptr, len) at the matching
                // offsets.
                sb.Append("\n");
                sb.Append("                string? lifted;\n");
                sb.Append("\n");
                sb.Append("                switch (new Span<byte>((void*)(ptr + 0), 1)[0]) {\n");
                sb.Append("                    case 0: {\n");
                sb.Append("                        lifted = null;\n");
                sb.Append("                        break;\n");
                sb.Append("                    }\n");
                sb.Append("\n");
                sb.Append("                    case 1: {\n");
                sb.Append("\n");
                sb.Append("                        lifted = Encoding.UTF8.GetString((byte*)BitConverter.ToInt32(new Span<byte>((void*)(ptr + 4), 4)), BitConverter.ToInt32(new Span<byte>((void*)(ptr + 8), 4)));\n");
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

        /// <summary>True for <c>result&lt;_, _&gt;</c> (both sides
        /// absent) — the direct i32-discriminant return shape.</summary>
        internal static bool IsElidedResultType(CtValType? t) =>
            t is CtResultType r && r.Ok == null && r.Err == null;

        /// <summary>
        /// Emit the <c>Result&lt;None, None&gt;</c> switch + throw
        /// body that follows the stub call in the totally-elided
        /// result path. Assumes the stub's int return was captured
        /// into the local named <c>result</c>. Shared by the free-
        /// function and resource-method emitters.
        /// </summary>
        internal static void EmitElidedResultTail(StringBuilder sb)
        {
            sb.Append("\n");
            var worldNs = EmitAmbient.WorldNamespace ?? "UNSET_WORLD_NAMESPACE";
            sb.Append("            Result<None, None> lifted;\n");
            sb.Append("\n");
            sb.Append("            switch (result) {\n");
            sb.Append("                case 0: {\n");
            sb.Append("\n");
            sb.Append("                    lifted = Result<None, None>.ok(new global::")
              .Append(worldNs).Append(".None());\n");
            sb.Append("                    break;\n");
            sb.Append("                }\n");
            sb.Append("                case 1: {\n");
            sb.Append("\n");
            sb.Append("                    lifted = Result<None, None>.err(new global::")
              .Append(worldNs).Append(".None());\n");
            sb.Append("                    break;\n");
            sb.Append("                }\n");
            sb.Append("\n");
            sb.Append("                default: throw new ArgumentException($\"invalid discriminant: {result}\");\n");
            sb.Append("            }\n");
            sb.Append("            if (lifted.IsOk) {\n");
            sb.Append("                var tmp = lifted.AsOk;\n");
            sb.Append("                return ;\n");
            sb.Append("            } else {\n");
            sb.Append("                throw new WitException(lifted.AsErr!, 0);\n");
            sb.Append("            }\n");
        }

        /// <summary>
        /// Emit the wrapper body for <c>result&lt;_, _&gt;</c> — the
        /// totally-elided case. Stub returns i32 directly (no return
        /// area); wrapper captures it, switches on it to build a
        /// <c>Result&lt;None, None&gt;</c>, then branches on
        /// <c>IsOk</c>: ok → <c>return ;</c>; err → <c>throw new
        /// WitException(lifted.AsErr!, 0)</c>.
        /// </summary>
        private static void EmitElidedResultWrapperBody(StringBuilder sb,
                                                         string stubClassName,
                                                         string methodName,
                                                         CtFunctionType sig)
        {
            // Direct-call capture (no blank between `{` and this line).
            sb.Append("            var result =  ");
            sb.Append(stubClassName).Append(".wasmImport").Append(methodName);
            sb.Append('(');
            EmitLoweredArgs(sb, sig);
            sb.Append(");\n");
            EmitElidedResultTail(sb);
        }

        /// <summary>
        /// Emit the lift of a variant from the return area into a
        /// local named <paramref name="varName"/>. Shape:
        /// <code>
        /// {QualifiedVariantType} {varName};
        /// switch (ptr+discOffset byte) {
        ///     case 0: {varName} = Type.case0(payload lift at payloadOffset); break;
        ///     case 1: {varName} = Type.case1(); break;   // no-payload
        ///     ...
        ///     default: throw …;
        /// }
        /// </code>
        /// The <paramref name="varName"/> must be declared
        /// inside the caller's lexical scope but initialized here.
        /// The caller decides what to do with it (return, wrap,
        /// etc.). <paramref name="indent"/> is the leading whitespace
        /// applied to every emitted line (e.g. 16 spaces inside a
        /// fixed-area block, 24 spaces inside a nested switch arm).
        /// </summary>
        private static void EmitVariantReturnAreaLift(
            StringBuilder sb,
            CtTypeRef variantRef,
            int discOffset,
            int payloadOffset,
            string varName,
            string indent)
        {
            var target = ResolveVariant(variantRef);
            var v = (CtVariantType)target.Type;
            var qualified = TypeRefEmit.EmitQualifiedPath(target);

            sb.Append(indent).Append(qualified).Append(' ').Append(varName).Append(";\n");
            sb.Append("\n");
            sb.Append(indent).Append("switch (new Span<byte>((void*)(ptr + ")
              .Append(discOffset).Append("), 1)[0]) {\n");

            var caseIndent = indent + "    ";
            var bodyIndent = indent + "        ";
            for (int i = 0; i < v.Cases.Count; i++)
            {
                var c = v.Cases[i];
                sb.Append(caseIndent).Append("case ").Append(i).Append(": {\n");

                var factoryName = NameConventions.ToCamelCase(c.Name);
                if (c.Payload != null && IsOwnedResource(c.Payload))
                {
                    // Resource-handle payload: construct the wrapper
                    // class inline as a named local, then feed it to
                    // the variant's static factory. No blank line
                    // before `var resource`; blank between the
                    // `resource` decl and the variant assignment.
                    var resTarget = ResolveOwnedResource(c.Payload);
                    var resQualified = TypeRefEmit.EmitQualifiedPath(resTarget);
                    sb.Append(bodyIndent).Append("var resource = new ")
                      .Append(resQualified).Append("(new ")
                      .Append(resQualified).Append(".THandle(BitConverter.ToInt32(new Span<byte>((void*)(ptr + ")
                      .Append(payloadOffset).Append("), 4))));\n");
                    sb.Append("\n");
                    sb.Append(bodyIndent).Append(varName).Append(" = ").Append(qualified)
                      .Append('.').Append(factoryName).Append("(resource);\n");
                }
                else
                {
                    sb.Append("\n");
                    sb.Append(bodyIndent).Append(varName).Append(" = ").Append(qualified)
                      .Append('.').Append(factoryName).Append('(');
                    if (c.Payload != null)
                    {
                        var payKind = ((CtPrimType)c.Payload).Kind;
                        sb.Append(EmitReturnAreaPrimLift(payKind, payloadOffset));
                    }
                    sb.Append(");\n");
                }
                sb.Append(bodyIndent).Append("break;\n");
                sb.Append(caseIndent).Append("}\n");
            }
            sb.Append("\n");
            sb.Append(caseIndent).Append("default: throw new ArgumentException($\"invalid discriminant: {new Span<byte>((void*)(ptr + ")
              .Append(discOffset).Append("), 1)[0]}\");\n");
            sb.Append(indent).Append("}\n");
        }

        /// <summary>
        /// Emit the return-area lift for <c>result&lt;Ok, Err&gt;</c>
        /// where each side is either a small primitive or None
        /// (absent). Shape:
        /// <code>
        /// Result&lt;Ok, Err&gt; lifted;
        /// switch (disc-byte) {
        ///     case 0: { lifted = Result&lt;...&gt;.ok(payload-or-None); break; }
        ///     case 1: { lifted = Result&lt;...&gt;.err(payload-or-None); break; }
        ///     default: throw new ArgumentException($"...");
        /// }
        /// if (lifted.IsOk) { var tmp = lifted.AsOk; return tmp; }
        /// else { throw new WitException(lifted.AsErr!, 0); }
        /// </code>
        /// The wrapper return type is the Ok type (or <c>void</c>
        /// when Ok is None — in which case we still emit
        /// <c>var tmp = lifted.AsOk; return ;</c> to match
        /// wit-bindgen's formatting quirk).
        /// </summary>
        private static void EmitResultReturnAreaLift(StringBuilder sb, CtResultType r, int payloadOffset)
        {
            sb.Append("\n");
            var okCs  = r.Ok  != null ? ResultArmTypeName(r.Ok)  : "None";
            var errCs = r.Err != null ? ResultArmTypeName(r.Err) : "None";
            var resultTy = "Result<" + okCs + ", " + errCs + ">";

            // Outer var name: when either arm needs a
            // multi-statement body (variant nested switch or
            // list-ptr/len lift into a local `array`), rename the
            // outer to `liftedResult` to avoid name collision.
            // Simple cases keep the byte-for-byte `lifted` name.
            bool nested = NeedsMultiStatementArm(r.Ok)
                       || NeedsMultiStatementArm(r.Err);
            var outerVar = nested ? "liftedResult" : "lifted";

            sb.Append("                ").Append(resultTy).Append(' ')
              .Append(outerVar).Append(";\n");
            sb.Append("\n");
            sb.Append("                switch (new Span<byte>((void*)(ptr + 0), 1)[0]) {\n");
            EmitResultArmBody(sb, "ok", r.Ok, resultTy, outerVar,
                              discCase: 0, payloadOffset: payloadOffset);
            EmitResultArmBody(sb, "err", r.Err, resultTy, outerVar,
                              discCase: 1, payloadOffset: payloadOffset);
            sb.Append("\n");
            sb.Append("                    default: throw new ArgumentException($\"invalid discriminant: {new Span<byte>((void*)(ptr + 0), 1)[0]}\");\n");
            sb.Append("                }\n");
            // Unwrap: ok → return (unwrapped or empty), err → throw.
            sb.Append("                if (").Append(outerVar).Append(".IsOk) {\n");
            sb.Append("                    var tmp = ").Append(outerVar).Append(".AsOk;\n");
            if (r.Ok != null)
                sb.Append("                    return tmp;\n");
            else
                sb.Append("                    return ;\n");   // wit-bindgen quirk: space before semi
            sb.Append("                } else {\n");
            sb.Append("                    throw new WitException(")
              .Append(outerVar).Append(".AsErr!, 0);\n");
            sb.Append("                }\n");
        }

        private static bool NeedsMultiStatementArm(CtValType? t) =>
            t != null && (IsVariantOfSmallPrimOrNone(t) || IsListOfPrim(t));

        /// <summary>Name to use for a result arm's Ok/Err type
        /// parameter. Primitives use their C# name; variants use
        /// the fully-qualified global:: path; list&lt;prim&gt; uses
        /// <c>primCs[]</c>; None for absent.</summary>
        private static string ResultArmTypeName(CtValType t)
        {
            if (t is CtPrimType p) return TypeRefEmit.EmitPrim(p.Kind);
            if (IsVariantOfSmallPrimOrNone(t))
                return TypeRefEmit.EmitQualifiedPath(ResolveVariant((CtTypeRef)t));
            if (IsListOfPrim(t))
            {
                var elemKind = ((CtPrimType)((CtListType)t).Element).Kind;
                return TypeRefEmit.EmitPrim(elemKind) + "[]";
            }
            throw new NotImplementedException(
                "ResultArmTypeName for " + t.GetType().Name + " is a follow-up.");
        }

        /// <summary>
        /// Emit one arm of the outer result switch. Simple cases
        /// (None / primitive) collapse to a single-line assignment
        /// via <c>outer = Result.&lt;factory&gt;(&lt;expr&gt;);</c>. Variant
        /// arms emit a nested switch building a local <c>lifted</c>,
        /// then assign the outer from it.
        /// </summary>
        private static void EmitResultArmBody(StringBuilder sb,
                                              string factory,
                                              CtValType? arm,
                                              string resultTy,
                                              string outerVar,
                                              int discCase,
                                              int payloadOffset)
        {
            sb.Append("                    case ").Append(discCase).Append(": {\n");
            sb.Append("\n");
            if (arm != null && IsVariantOfSmallPrimOrNone(arm))
            {
                // Nested variant lift — declare inner `lifted`, run
                // switch-by-disc-byte at payloadOffset (variant disc
                // sits at result-payload start), then wrap the result
                // into outer via Result.factory(lifted).
                EmitVariantReturnAreaLift(
                    sb, (CtTypeRef)arm,
                    discOffset: payloadOffset,
                    payloadOffset: payloadOffset + 4,
                    varName: "lifted",
                    indent: "                        ");
                sb.Append("\n");
                sb.Append("                        ").Append(outerVar)
                  .Append(" = ").Append(resultTy).Append('.').Append(factory)
                  .Append("(lifted);\n");
            }
            else if (arm != null && IsListOfPrim(arm))
            {
                // list<prim> arm — allocate managed array of the
                // reported length, then span-copy guest memory into
                // it. (ptr, len) pair lives at payloadOffset and
                // payloadOffset+4. Trailing blank line matches
                // wit-bindgen's formatting before the outer
                // assignment.
                var elemKind = ((CtPrimType)((CtListType)arm).Element).Kind;
                var elemCs = TypeRefEmit.EmitPrim(elemKind);
                sb.Append("                        var array = new ").Append(elemCs)
                  .Append("[BitConverter.ToInt32(new Span<byte>((void*)(ptr + ")
                  .Append(payloadOffset + 4).Append("), 4))];\n");
                sb.Append("                        new Span<").Append(elemCs)
                  .Append(">((void*)(BitConverter.ToInt32(new Span<byte>((void*)(ptr + ")
                  .Append(payloadOffset)
                  .Append("), 4))), BitConverter.ToInt32(new Span<byte>((void*)(ptr + ")
                  .Append(payloadOffset + 4).Append("), 4))).CopyTo(new Span<")
                  .Append(elemCs).Append(">(array));\n");
                sb.Append("\n");
                sb.Append("                        ").Append(outerVar)
                  .Append(" = ").Append(resultTy).Append('.').Append(factory)
                  .Append("(array);\n");
            }
            else
            {
                sb.Append("                        ").Append(outerVar)
                  .Append(" = ").Append(resultTy).Append('.').Append(factory).Append('(');
                sb.Append(EmitResultArmExpr(arm, payloadOffset));
                sb.Append(");\n");
            }
            sb.Append("                        break;\n");
            sb.Append("                    }\n");
        }

        /// <summary>
        /// Build the payload expression inside a simple
        /// <c>Result.&lt;factory&gt;(…)</c> call. None → <c>new
        /// global::{WorldNs}.None()</c>; primitive → the standard
        /// return-area prim lift.
        /// </summary>
        private static string EmitResultArmExpr(CtValType? side, int offset)
        {
            if (side == null)
            {
                var worldNs = EmitAmbient.WorldNamespace ?? "UNSET_WORLD_NAMESPACE";
                return "new global::" + worldNs + ".None()";
            }
            return EmitReturnAreaPrimLift(((CtPrimType)side).Kind, offset);
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
                // 64-bit reads — 8-byte span. The payload slot is
                // already aligned to 8 by the return-area layout.
                CtPrim.S64 =>
                    $"BitConverter.ToInt64(new Span<byte>((void*)({off}), 8))",
                CtPrim.U64 =>
                    $"unchecked((ulong)(BitConverter.ToInt64(new Span<byte>((void*)({off}), 8))))",
                CtPrim.F64 =>
                    $"BitConverter.ToDouble(new Span<byte>((void*)({off}), 8))",
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
        internal static bool HasPrelude(CtFunctionType sig)
        {
            foreach (var p in sig.Params)
            {
                if (p.Type is CtPrimType prim && prim.Kind == CtPrim.String)
                    return true;
                if (IsListOfPrim(p.Type)) return true;
                if (IsOptionOfSmallPrim(p.Type)) return true;
                if (IsOptionOfString(p.Type)) return true;
                if (IsVariantOfSmallPrimOrNone(p.Type)) return true;
                if (IsListOfResourceRef(p.Type)) return true;
            }
            return false;
        }

        /// <summary>True when any param pins a GCHandle that needs
        /// to be freed before the wrapper returns. Currently only
        /// <c>list&lt;R&gt;</c> (resource-ref list) allocates a
        /// pinned byte buffer; strings use a different pinning
        /// mechanism that doesn't require explicit Free.</summary>
        internal static bool HasPinnedHandleParam(CtFunctionType sig)
        {
            foreach (var p in sig.Params)
                if (IsListOfResourceRef(p.Type)) return true;
            return false;
        }

        /// <summary>
        /// Emit <c>gcHandle.Free();</c> for each pinned-handle
        /// param, at the indent the caller specifies. Called just
        /// before the final <c>return</c> inside the return-area
        /// body (or before the <c>if</c> branch in result-unwrap
        /// tail). No-op when the signature has no pinned handles.
        /// </summary>
        internal static void EmitPinnedHandleCleanup(StringBuilder sb,
                                                     CtFunctionType sig,
                                                     string indent)
        {
            // Single-handle case — `gcHandle`. Multi-handle would
            // count through `gcHandle`, `gcHandle1`, … but we
            // don't emit multi yet; a second resource-list param
            // would need an allocator-bump here.
            foreach (var p in sig.Params)
            {
                if (IsListOfResourceRef(p.Type))
                {
                    sb.Append(indent).Append("gcHandle.Free();\n");
                    return;
                }
            }
        }

        /// <summary>True when any param is a resource-ref — the
        /// wrapper body gets per-param <c>var handle{N} = arg.Handle;</c>
        /// extraction emitted before any other prelude work.</summary>
        internal static bool HasResourceRefParam(CtFunctionType sig)
        {
            foreach (var p in sig.Params)
                if (IsResourceRef(p.Type)) return true;
            return false;
        }

        /// <summary>
        /// Emit the resource-handle extraction block that precedes
        /// all other wrapper-body code. For each resource-ref
        /// param, emit <c>var handle{N} = arg.Handle;</c>
        /// (counter-indexed: 0-th slot gets bare <c>handle</c>,
        /// subsequent slots get <c>handle0</c>, <c>handle1</c>, …).
        /// For <c>own&lt;R&gt;</c> params, also emit <c>arg.Handle = 0;</c>
        /// to transfer ownership. <paramref name="startingSlot"/> is
        /// the slot counter value; resource methods pass 1 because
        /// their own <c>this.Handle</c> already occupies slot 0.
        /// </summary>
        internal static void EmitResourceRefHandles(StringBuilder sb,
                                                     CtFunctionType sig,
                                                     int startingSlot = 0)
        {
            int slot = startingSlot;
            foreach (var p in sig.Params)
            {
                if (!IsResourceRef(p.Type)) continue;
                var argName = NameConventions.ToCamelCase(p.Name);
                sb.Append("            var ").Append(HandleLocalName(slot))
                  .Append(" = ").Append(argName).Append(".Handle;\n");
                // Ownership transfer: only for own<T>, not borrow<T>.
                if (IsOwnedResource(p.Type))
                {
                    sb.Append("            ").Append(argName).Append(".Handle = 0;\n");
                }
                slot++;
            }
        }

        /// <summary>Name of the k-th handle slot local: bare
        /// <c>handle</c> for slot 0, <c>handle{k-1}</c> for k ≥ 1.
        /// Matches wit-bindgen 0.30.0's counter.</summary>
        internal static string HandleLocalName(int slot) =>
            slot == 0 ? "handle" : ("handle" + (slot - 1));

        internal static void EmitWrapperPrelude(StringBuilder sb,
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
                else if (IsOptionOfString(p.Type))
                {
                    // option<string> lowers to (disc, ptr, len).
                    // Three locals — per-arg named Tag / Ptr / Len
                    // — populated from a Some/None branch. Some
                    // path pins the UTF-8 GCHandle via
                    // InteropString.FromString; None zeros all
                    // three slots.
                    sb.Append("            int ").Append(argName).Append("Tag;\n");
                    sb.Append("            nint ").Append(argName).Append("Ptr;\n");
                    sb.Append("            int ").Append(argName).Append("Len;\n");
                    sb.Append("            if (").Append(argName).Append(" != null) {\n");
                    sb.Append("                ").Append(argName).Append("Tag = 1;\n");
                    sb.Append("                ").Append(argName).Append("Ptr = ");
                    sb.Append("InteropString.FromString(").Append(argName);
                    sb.Append(", out ").Append(argName).Append("Len).ToInt32();\n");
                    sb.Append("            } else {\n");
                    sb.Append("                ").Append(argName).Append("Tag = 0;\n");
                    sb.Append("                ").Append(argName).Append("Ptr = 0;\n");
                    sb.Append("                ").Append(argName).Append("Len = 0;\n");
                    sb.Append("            }\n");
                }
                else if (IsListOfResourceRef(p.Type))
                {
                    // list<R> (R a resource ref) — allocate a
                    // byte[4*Count] buffer, GCHandle-pin it, then
                    // loop over the C# List<R> writing each
                    // element's .Handle as an i32 at index*4.
                    // Stub takes (buffer-address, count); the pin
                    // is freed in the post-call tail (see
                    // EmitPinnedHandleCleanup).
                    sb.Append("            byte[] buffer = new byte[4 * ")
                      .Append(argName).Append(".Count];\n");
                    sb.Append("            var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);\n");
                    sb.Append("            var address = gcHandle.AddrOfPinnedObject();\n");
                    sb.Append("\n");
                    sb.Append("            for (int index = 0; index < ")
                      .Append(argName).Append(".Count; ++index) {\n");
                    var elTarget = ResolveOwnedResource(((CtListType)p.Type).Element);
                    var elQualified = TypeRefEmit.EmitQualifiedPath(elTarget);
                    sb.Append("                ").Append(elQualified)
                      .Append(" element = ").Append(argName).Append("[index];\n");
                    sb.Append("                int basePtr = (int)address + (index * 4);\n");
                    sb.Append("                var handle = element.Handle;\n");
                    sb.Append("                BitConverter.TryWriteBytes(new Span<byte>((void*)(basePtr + 0), 4), unchecked((int)handle));\n");
                    sb.Append("\n");
                    sb.Append("            }\n");
                }
                else if (IsVariantOfSmallPrimOrNone(p.Type))
                {
                    // variant<…> param lowers to (i32 disc, i32
                    // payload). Switch on arg.Tag to emit one case
                    // per variant case: payload-carrying arms
                    // unwrap via arg.AsCaseName and cast into the
                    // payload slot; no-payload arms zero it.
                    //
                    // Local names diverge from wit-bindgen's
                    // shared-counter scheme (`lowered` / `lowered6`);
                    // per-arg `{arg}Tag` / `{arg}Val` is clearer.
                    var vTarget = ResolveVariant((CtTypeRef)p.Type);
                    var variant = (CtVariantType)vTarget.Type;
                    sb.Append("            int ").Append(argName).Append("Tag;\n");
                    sb.Append("            int ").Append(argName).Append("Val;\n");
                    sb.Append("\n");
                    sb.Append("            switch (").Append(argName).Append(".Tag) {\n");
                    for (int i = 0; i < variant.Cases.Count; i++)
                    {
                        var c = variant.Cases[i];
                        sb.Append("                case ").Append(i).Append(": {\n");
                        if (c.Payload != null)
                        {
                            var payKind = ((CtPrimType)c.Payload).Kind;
                            var asProp = "As" + NameConventions.ToPascalCase(c.Name);
                            var payName = TypeRefEmit.EmitPrim(payKind);
                            sb.Append("                    ").Append(payName)
                              .Append(" payload = ").Append(argName).Append('.')
                              .Append(asProp).Append(";\n");
                            sb.Append("\n");
                            sb.Append("                    ").Append(argName)
                              .Append("Tag = ").Append(i).Append(";\n");
                            sb.Append("                    ").Append(argName)
                              .Append("Val = ").Append(EmitLower(c.Payload, "payload"))
                              .Append(";\n");
                        }
                        else
                        {
                            sb.Append("\n");
                            sb.Append("                    ").Append(argName)
                              .Append("Tag = ").Append(i).Append(";\n");
                            sb.Append("                    ").Append(argName)
                              .Append("Val = 0;\n");
                        }
                        sb.Append("\n");
                        sb.Append("                    break;\n");
                        sb.Append("                }\n");
                    }
                    sb.Append("\n");
                    sb.Append("                default: throw new ArgumentException($\"invalid discriminant: {")
                      .Append(argName).Append("}\");\n");
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
        /// Integer stubs take <c>0</c>; f32 stubs take <c>0f</c>;
        /// long stubs take <c>0L</c>; double stubs take <c>0d</c>.
        /// </summary>
        private static string OptionPayloadZero(string stubType) => stubType switch
        {
            "float" => "0f",
            "long" => "0L",
            "double" => "0d",
            _ => "0",
        };

        // ---- Stub type mapping (core wasm ABI) -----------------------------

        /// <summary>
        /// Stub-side core-wasm-ABI type(s) for one wrapper param.
        /// Most primitives lower to a single i32/i64/f32/f64. Strings
        /// and <c>list&lt;u8&gt;</c> lower to two stub params:
        /// <c>nint ptr, int len</c>. Other aggregates are follow-ups.
        /// </summary>
        internal static string[] StubTypesFor(CtValType t)
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
            if (IsListOfResourceRef(t))
            {
                // list<R> (R a resource) lowers to (buffer-ptr, count)
                // — same wire shape as list<prim> (ptr+len). The
                // wrapper prelude builds a pinned i32-handle array
                // from the `List<R>` and passes the pinned address.
                return new[] { "nint", "int" };
            }
            if (IsOptionOfSmallPrim(t))
            {
                // option<P> lowers to (discriminant, payload). The
                // discriminant is always an i32; the payload slot
                // takes the inner primitive's stub type — `long`
                // for 64-bit payloads, `int`/`float`/etc. for
                // smaller ones.
                var inner = ((CtOptionType)t).Inner;
                var innerStub = StubTypesFor(inner);
                return new[] { "int", innerStub[0] };
            }
            if (IsOptionOfString(t))
            {
                // option<string> lowers to (disc, ptr, len) —
                // concat of i32 discriminant + string's flat
                // representation.
                return new[] { "int", "nint", "int" };
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
            if (IsRecordOfSmallPrims(t))
            {
                // record { f1: P1, f2: P2, … } — identical flattening
                // to tuple; one word per small-primitive field.
                var rec = (CtRecordType)ResolveRecord((CtTypeRef)t).Type;
                var flat = new string[rec.Fields.Count];
                for (int i = 0; i < rec.Fields.Count; i++)
                    flat[i] = StubTypesFor(rec.Fields[i].Type)[0];
                return flat;
            }
            if (IsOwnedResource(t) || IsBorrowedResource(t))
            {
                // own<R> / borrow<R> both lower to an i32 handle on
                // the wire; ownership semantics only affect the
                // param-side extraction (own zeroes the caller's
                // handle field; borrow leaves it alone).
                return new[] { "int" };
            }
            if (IsVariantOfSmallPrimOrNone(t))
            {
                // variant with ≤1-small-prim-payload cases lowers
                // to (i32 disc, i32 payload). No-payload cases
                // pass 0 in the payload slot.
                return new[] { "int", "int" };
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
            && IsFlatPrim(op.Kind);

        /// <summary>
        /// True when <paramref name="t"/> is <c>option&lt;string&gt;</c>.
        /// Lowering adds a 3-slot stub signature (disc + ptr + len) +
        /// 3-uint return area. Param prelude allocates the pinned
        /// UTF-8 handle inside the Some branch; None branch zeros.
        /// </summary>
        internal static bool IsOptionOfString(CtValType t) =>
            t is CtOptionType o
            && o.Inner is CtPrimType op
            && op.Kind == CtPrim.String;

        /// <summary>
        /// True when <paramref name="t"/> is <c>result&lt;Ok, Err&gt;</c>
        /// where each side is either absent (None), a small primitive,
        /// or a variant-with-small-prim-or-none cases. Excludes the
        /// totally-elided <c>result&lt;_, _&gt;</c> form — that uses
        /// a direct i32-discriminant return (no return area) and
        /// takes a different code path.
        /// </summary>
        internal static bool IsResultOfPrimOrNone(CtValType t)
        {
            if (!(t is CtResultType r)) return false;
            if (r.Ok == null && r.Err == null) return false;   // elided case
            if (r.Ok != null && !IsResultArmEmitable(r.Ok)) return false;
            if (r.Err != null && !IsResultArmEmitable(r.Err)) return false;
            return true;
        }

        private static bool IsResultArmEmitable(CtValType t)
        {
            if (t is CtPrimType p) return IsFlatPrim(p.Kind);
            if (IsVariantOfSmallPrimOrNone(t)) return true;
            if (IsListOfPrim(t)) return true;
            return false;
        }

        /// <summary>Payload byte size for a result Ok/Err side.
        /// None = 0, 32-bit primitive = 4, 64-bit primitive = 8,
        /// variant-with-small-prim-or-none cases = 8 (variant disc
        /// at +0 + 1-word payload at +4 — effective max extent
        /// within its arm is 8 bytes), list&lt;prim&gt; = 8 (ptr +
        /// len).</summary>
        private static int ResultArmBytes(CtValType? t)
        {
            if (t == null) return 0;
            if (t is CtPrimType p)
                return IsWidePrim(p.Kind) ? 8 : 4;
            if (IsVariantOfSmallPrimOrNone(t)) return 8;
            if (IsListOfPrim(t)) return 8;
            return 0;
        }

        /// <summary>Byte alignment for a result Ok/Err side —
        /// drives the outer payload offset (rounded up from the
        /// 1-byte discriminant). 32-bit primitive = 4-byte align;
        /// 64-bit primitive = 8-byte align; variant = 4 (its own
        /// disc aligns to 4, payload slot is 32-bit); list = 4
        /// (ptr and len are both 32-bit).</summary>
        private static int ResultArmAlign(CtValType? t)
        {
            if (t == null) return 1;
            if (t is CtPrimType p)
                return IsWidePrim(p.Kind) ? 8 : 4;
            if (IsVariantOfSmallPrimOrNone(t)) return 4;
            if (IsListOfPrim(t)) return 4;
            return 1;
        }

        /// <summary>
        /// True when <paramref name="t"/> denotes an owned handle
        /// to a resource — either explicitly as <c>own&lt;R&gt;</c>
        /// or as a bare <c>R</c> identifier (the WIT grammar treats
        /// a resource reference in a value-type position as
        /// implicitly <c>own</c>). The resource handle lowers to an
        /// i32 on the wire; at the C# layer the wrapper constructs
        /// <c>new Resource(new Resource.THandle(int))</c> to package
        /// it back into the resource wrapper class.
        /// </summary>
        internal static bool IsOwnedResource(CtValType t)
        {
            CtTypeRef? r = t switch
            {
                CtOwnType o => o.Resource as CtTypeRef,
                CtTypeRef tr => tr,
                _ => null,
            };
            return IsResourceRefCore(r);
        }

        /// <summary>
        /// True when <paramref name="t"/> is a borrowed handle to
        /// a resource (<c>borrow&lt;R&gt;</c>). At the wire level
        /// the handle is identical to <c>own&lt;R&gt;</c> — an i32
        /// slot — but the param-side wrapper does NOT null out the
        /// caller's handle field (ownership stays with the caller).
        /// </summary>
        internal static bool IsBorrowedResource(CtValType t)
        {
            if (!(t is CtBorrowType b)) return false;
            return IsResourceRefCore(b.Resource as CtTypeRef);
        }

        /// <summary>Either <see cref="IsOwnedResource"/> or
        /// <see cref="IsBorrowedResource"/> — any i32-handle
        /// resource reference usable as a value-type slot.</summary>
        internal static bool IsResourceRef(CtValType t) =>
            IsOwnedResource(t) || IsBorrowedResource(t);

        /// <summary>
        /// True when <paramref name="t"/> is <c>list&lt;R&gt;</c>
        /// where <c>R</c> is a resource reference (own, borrow, or
        /// bare identifier pointing at a resource). C# type:
        /// <c>System.Collections.Generic.List&lt;R&gt;</c>. Lowered
        /// on the wire to a pinned <c>byte[count*4]</c> buffer of
        /// i32 handles + the element count.
        /// </summary>
        internal static bool IsListOfResourceRef(CtValType t) =>
            t is CtListType l && IsResourceRef(l.Element);

        private static bool IsResourceRefCore(CtTypeRef? r)
        {
            if (r == null || r.Target == null) return false;
            var target = r.Target;
            while (target.Type is CtTypeRef innerRef
                   && innerRef.Target != null
                   && innerRef.Target != target)
            {
                target = innerRef.Target;
            }
            return target.Type is CtResourceType;
        }

        /// <summary>Resolve any resource-ref-shaped type (own /
        /// borrow / bare identifier) to its concrete resource
        /// named-type with the alias chain unwound.</summary>
        private static CtNamedType ResolveOwnedResource(CtValType t)
        {
            var r = t switch
            {
                CtOwnType o => (CtTypeRef)o.Resource,
                CtBorrowType b => (CtTypeRef)b.Resource,
                CtTypeRef tr => tr,
                _ => throw new System.InvalidOperationException(
                    "ResolveOwnedResource called on " + t.GetType().Name),
            };
            var target = r.Target!;
            while (target.Type is CtTypeRef innerRef
                   && innerRef.Target != null
                   && innerRef.Target != target)
            {
                target = innerRef.Target;
            }
            return target;
        }

        /// <summary>
        /// True when <paramref name="t"/> is a named-type reference
        /// whose target is a <c>variant</c> with every case either
        /// payloadless or carrying a single small primitive. Lowered
        /// form: 1-word discriminant + 1-word payload (the payload
        /// slot is wide enough for the largest case). No-payload
        /// cases zero the payload slot.
        /// </summary>
        internal static bool IsVariantOfSmallPrimOrNone(CtValType t)
        {
            if (!(t is CtTypeRef r) || r.Target == null) return false;
            var target = r.Target;
            while (target.Type is CtTypeRef innerRef
                   && innerRef.Target != null
                   && innerRef.Target != target)
            {
                target = innerRef.Target;
            }
            if (!(target.Type is CtVariantType v)) return false;
            foreach (var c in v.Cases)
            {
                if (c.Payload == null) continue;
                if (IsVariantPayloadEmitable(c.Payload)) continue;
                return false;
            }
            return true;
        }

        /// <summary>True for variant case payloads the emitter
        /// handles: small primitives (≤ 32-bit), and owned-resource
        /// handles (cross-interface or same-interface). Wide
        /// primitives, strings, lists, aggregates are a follow-up
        /// (layout grows past the current 1-word payload slot).</summary>
        private static bool IsVariantPayloadEmitable(CtValType t)
        {
            if (t is CtPrimType p) return IsSmallPrim(p.Kind);
            if (IsOwnedResource(t)) return true;
            return false;
        }

        /// <summary>Resolve a variant type-ref to its concrete
        /// named-type (alias chain unwound).</summary>
        private static CtNamedType ResolveVariant(CtTypeRef r)
        {
            var target = r.Target!;
            while (target.Type is CtTypeRef innerRef
                   && innerRef.Target != null
                   && innerRef.Target != target)
            {
                target = innerRef.Target;
            }
            return target;
        }

        /// <summary>
        /// True when <paramref name="t"/> is a named-type reference
        /// whose target is a <c>record</c> with every field a small
        /// primitive (≤ 1 core-wasm word). Same-shape as
        /// <see cref="IsTupleOfSmallPrims"/> — flat field-by-field
        /// lowering, one-word-per-field return area. Records with
        /// aggregate or wide-primitive fields need stride/alignment
        /// handling and are a follow-up.
        /// </summary>
        internal static bool IsRecordOfSmallPrims(CtValType t)
        {
            if (!(t is CtTypeRef r) || r.Target == null) return false;
            // Follow a chain of same-interface alias refs to the
            // concrete definition — matches TypeRefEmit's resolution.
            var target = r.Target;
            while (target.Type is CtTypeRef innerRef
                   && innerRef.Target != null
                   && innerRef.Target != target)
            {
                target = innerRef.Target;
            }
            if (!(target.Type is CtRecordType rec)) return false;
            if (rec.Fields.Count == 0) return false;
            foreach (var f in rec.Fields)
            {
                if (!(f.Type is CtPrimType fp)) return false;
                if (!IsSmallPrim(fp.Kind)) return false;
            }
            return true;
        }

        /// <summary>
        /// Resolve a <see cref="CtTypeRef"/> through any alias
        /// chain to the concrete <see cref="CtNamedType"/>. Used by
        /// record emission to reach the record definition for field
        /// iteration and for the qualified type name.
        /// </summary>
        private static CtNamedType ResolveRecord(CtTypeRef r)
        {
            var target = r.Target!;
            while (target.Type is CtTypeRef innerRef
                   && innerRef.Target != null
                   && innerRef.Target != target)
            {
                target = innerRef.Target;
            }
            return target;
        }

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

        /// <summary>8-byte-aligned primitive — s64/u64/f64. Relevant
        /// for return-area layout: a payload containing any wide
        /// prim forces the whole return area to <c>ulong[]</c>
        /// backing with payload offset rounded up to 8.</summary>
        private static bool IsWidePrim(CtPrim k) =>
            k == CtPrim.S64 || k == CtPrim.U64 || k == CtPrim.F64;

        /// <summary>Any primitive with a direct flat lowering
        /// (everything except string, which flattens to (ptr, len)
        /// and is handled separately).</summary>
        private static bool IsFlatPrim(CtPrim k) =>
            IsSmallPrim(k) || IsWidePrim(k);

        private static string EmitStubReturnType(CtFunctionType sig)
        {
            if (sig.HasNoResult) return "void";
            if (sig.Result == null) return "void";
            // Returns that lower to multiple words (string, list,
            // record, option, result with payload) get a return-area
            // pointer param on the stub instead — the stub's C#
            // return becomes void.
            if (UsesReturnArea(sig)) return "void";
            // result<_, _> (totally elided) — stub returns i32
            // discriminant directly, no payload and no return area.
            if (IsElidedResultType(sig.Result)) return "int";
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
            if (IsOptionOfString(sig.Result)) return true;
            if (IsTupleOfSmallPrims(sig.Result)) return true;
            if (IsRecordOfSmallPrims(sig.Result)) return true;
            if (IsResultOfPrimOrNone(sig.Result)) return true;
            if (IsVariantOfSmallPrimOrNone(sig.Result)) return true;
            return false;
        }

        /// <summary>
        /// Return-area byte size for a function's return type, in
        /// multiples of 4 bytes (uints). <c>string</c> and
        /// <c>list&lt;u8&gt;</c> = 2 (ptr + len). Other aggregates
        /// plug in as they're supported.
        /// </summary>
        /// <summary>
        /// Return-area allocation: backing type (<c>uint</c> or
        /// <c>ulong</c>), element count in that backing, and the
        /// byte offset at which the payload (for discriminated
        /// shapes) starts. For shapes without an outer discriminant
        /// (string / list / tuple / record), <c>PayloadOffset</c>
        /// is 0 and the elements live at the front.
        /// </summary>
        private readonly struct RetAreaInfo
        {
            public readonly string Backing;
            public readonly int Count;
            public readonly int PayloadOffset;
            public RetAreaInfo(string backing, int count, int payloadOffset)
            { Backing = backing; Count = count; PayloadOffset = payloadOffset; }
        }

        /// <summary>Byte size of a return-area backing element:
        /// 4 for <c>uint</c>, 8 for <c>ulong</c>.</summary>
        private static int BackingSize(string backing) =>
            backing == "ulong" ? 8 : 4;

        private static RetAreaInfo GetRetAreaInfo(CtFunctionType sig)
        {
            if (sig.Result is CtPrimType p && p.Kind == CtPrim.String)
                return new RetAreaInfo("uint", 2, 0);
            if (IsListOfPrim(sig.Result!))
                return new RetAreaInfo("uint", 2, 0);
            if (IsOptionOfString(sig.Result!))
                return new RetAreaInfo("uint", 3, 0);
            if (IsOptionOfSmallPrim(sig.Result!))
            {
                // option<prim>: disc at 0, payload at align(1, psz)
                // = 4 or 8. Total bytes = payload-off + payload-size.
                var k = ((CtPrimType)((CtOptionType)sig.Result!).Inner).Kind;
                bool wide = IsWidePrim(k);
                int payOff = wide ? 8 : 4;
                int total  = payOff + (wide ? 8 : 4);
                string bk  = wide ? "ulong" : "uint";
                return new RetAreaInfo(bk, total / BackingSize(bk), payOff);
            }
            if (IsResultOfPrimOrNone(sig.Result!))
            {
                // result<Ok, Err>: disc at 0, payload at align(1, A)
                // where A = max(okAlign, errAlign). Payload slot
                // size = max(okSize, errSize). Backing flips to
                // ulong[] when any arm forces 8-byte alignment.
                var rr = (CtResultType)sig.Result!;
                int align = System.Math.Max(
                    ResultArmAlign(rr.Ok), ResultArmAlign(rr.Err));
                int payOff = (1 + align - 1) / align * align;
                int size = System.Math.Max(
                    ResultArmBytes(rr.Ok), ResultArmBytes(rr.Err));
                int total = payOff + size;
                string bk = align == 8 ? "ulong" : "uint";
                int bs = BackingSize(bk);
                int count = (total + bs - 1) / bs;
                return new RetAreaInfo(bk, count, payOff);
            }
            if (IsTupleOfSmallPrims(sig.Result!))
                return new RetAreaInfo("uint",
                    ((CtTupleType)sig.Result!).Elements.Count, 0);
            if (IsRecordOfSmallPrims(sig.Result!))
                return new RetAreaInfo("uint",
                    ((CtRecordType)ResolveRecord((CtTypeRef)sig.Result!).Type).Fields.Count,
                    0);
            if (IsVariantOfSmallPrimOrNone(sig.Result!))
                return new RetAreaInfo("uint", 2, 4);
            throw new NotImplementedException(
                "Return area sizing for " + sig.Result?.GetType().Name +
                " is a follow-up.");
        }

        private static void EmitWrapperParams(StringBuilder sb, CtFunctionType sig)
        {
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                FunctionEmit.EmitParamWitName(sb, sig.Params[i].Name);
                sb.Append(TypeRefEmit.EmitParam(sig.Params[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(sig.Params[i].Name));
            }
        }

        internal static void EmitLoweredArgs(StringBuilder sb, CtFunctionType sig,
                                             int resHandleStartingSlot = 0)
        {
            bool first = true;
            int listIdx = 0;
            int resSlot = resHandleStartingSlot;
            foreach (var p in sig.Params)
            {
                var argName = NameConventions.ToCamelCase(p.Name);
                if (!first) sb.Append(", ");
                first = false;
                if (IsResourceRef(p.Type))
                {
                    // Handle extracted in the wrapper's resource-
                    // handle prelude; pass by its slot-indexed local
                    // name (handle / handle0 / handle1 / …).
                    sb.Append(HandleLocalName(resSlot));
                    resSlot++;
                    continue;
                }
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
                else if (IsListOfResourceRef(p.Type))
                {
                    // list<R> lowered to (pinned-address, count).
                    // Address + count come from the prelude's
                    // GCHandle-pinned buffer. wit-bindgen uses
                    // `@in.Count` (not `.Length`) since the C#
                    // type is System.Collections.Generic.List<R>.
                    sb.Append("(int)address, ").Append(argName).Append(".Count");
                }
                else if (IsOptionOfSmallPrim(p.Type))
                {
                    // option<prim> lowered by the prelude to
                    // `{arg}Tag, {arg}Val`.
                    sb.Append(argName).Append("Tag, ").Append(argName).Append("Val");
                }
                else if (IsOptionOfString(p.Type))
                {
                    // option<string> lowered by the prelude to
                    // `{arg}Tag, {arg}Ptr, {arg}Len`.
                    sb.Append(argName).Append("Tag, ")
                      .Append(argName).Append("Ptr, ")
                      .Append(argName).Append("Len");
                }
                else if (IsVariantOfSmallPrimOrNone(p.Type))
                {
                    // variant<…> lowered by the prelude to
                    // `{arg}Tag, {arg}Val`.
                    sb.Append(argName).Append("Tag, ")
                      .Append(argName).Append("Val");
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
                else if (IsRecordOfSmallPrims(p.Type))
                {
                    // record { f1: P1, f2: P2, … } lowered inline as
                    // `{Lower(P1, argName.f1)}, {Lower(P2, argName.f2)}, …`.
                    // Field names keep their WIT casing (wit-bindgen
                    // emits them verbatim as the field identifier).
                    var rec = (CtRecordType)ResolveRecord((CtTypeRef)p.Type).Type;
                    for (int i = 0; i < rec.Fields.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        var fieldArg = argName + "." + rec.Fields[i].Name;
                        sb.Append(EmitLower(rec.Fields[i].Type, fieldArg));
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
