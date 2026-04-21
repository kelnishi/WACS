// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Text
{
    public static partial class TextModuleParser
    {
        // ---- ValType parsing ----------------------------------------------
        //
        // A valtype token in WAT is one of:
        //   i32 | i64 | f32 | f64 | v128
        //   funcref | externref | anyref | eqref | i31ref | structref | arrayref
        //   nullfuncref | nullexternref | nullref | exnref | nullexnref
        //   (ref <heaptype>)         — non-nullable
        //   (ref null <heaptype>)    — nullable
        //
        // <heaptype> is one of:
        //   func | extern | any | eq | i31 | struct | array | exn | noexn
        //   nofunc | noextern | none
        //   <typeidx>                — $name or decimal index
        //

        /// <summary>
        /// Parse a single atom as a non-ref value type. Returns true on
        /// success; on failure, leaves the atom to the caller.
        /// </summary>
        internal static bool TryParseNumericValType(string text, out ValType type)
        {
            switch (text)
            {
                case "i32":  type = ValType.I32; return true;
                case "i64":  type = ValType.I64; return true;
                case "f32":  type = ValType.F32; return true;
                case "f64":  type = ValType.F64; return true;
                case "v128": type = ValType.V128; return true;
                default: type = default; return false;
            }
        }

        /// <summary>
        /// Parse a single atom as a reference-type shorthand (funcref etc.).
        /// </summary>
        internal static bool TryParseRefShorthand(string text, out ValType type)
        {
            switch (text)
            {
                case "funcref":       type = ValType.FuncRef;   return true;
                case "externref":     type = ValType.ExternRef; return true;
                case "anyref":        type = ValType.Any;       return true;
                case "eqref":         type = ValType.Eq;        return true;
                case "i31ref":        type = ValType.I31;       return true;
                case "structref":     type = ValType.Struct;    return true;
                case "arrayref":      type = ValType.Array;     return true;
                case "nullfuncref":   type = ValType.NoFunc;    return true;
                case "nullexternref": type = ValType.NoExtern;  return true;
                case "nullref":       type = ValType.None;      return true;
                case "exnref":        type = ValType.Exn;       return true;
                case "nullexnref":    type = ValType.NoExn;     return true;
                default: type = default; return false;
            }
        }

        /// <summary>
        /// Parse a heaptype atom — abstract name or a typeidx ($name / uint).
        /// Returns the underlying <see cref="ValType"/> (ref bits not yet set
        /// — callers apply Ref / Nullable).
        /// </summary>
        private static ValType ParseHeapType(TextParseContext ctx, SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected heap type");
            var text = atom.AtomText();
            switch (text)
            {
                case "func":    return ValType.FuncRef;
                case "extern":  return ValType.ExternRef;
                case "any":     return ValType.Any;
                case "eq":      return ValType.Eq;
                case "i31":     return ValType.I31;
                case "struct":  return ValType.Struct;
                case "array":   return ValType.Array;
                case "exn":     return ValType.Exn;
                case "noexn":   return ValType.NoExn;
                case "nofunc":  return ValType.NoFunc;
                case "noextern":return ValType.NoExtern;
                case "none":    return ValType.None;
            }
            // Otherwise it must be a typeidx ($name or uint).
            int idx = ResolveTypeIdx(ctx, atom);
            // DefType encoding: signed bit set + index value in low bits.
            // ValType.IsDefType returns true when Index().Value >= 0. Build
            // the raw int: index value (positive) in low bits, no ref bits.
            return (ValType)idx;
        }

        /// <summary>
        /// Parse a valtype node — may be a single atom or a (ref …) /
        /// (ref null …) list form.
        /// </summary>
        internal static ValType ParseValType(TextParseContext ctx, SExpr node)
        {
            if (node.Kind == SExprKind.Atom)
            {
                if (node.Token.Kind != TokenKind.Keyword)
                    throw new FormatException($"line {node.Token.Line}: expected valtype keyword");
                var text = node.AtomText();
                if (TryParseNumericValType(text, out var nt)) return nt;
                if (TryParseRefShorthand(text, out var rt))   return rt;
                throw new FormatException($"line {node.Token.Line}: unknown valtype '{text}'");
            }

            // List form — must be (ref ...) or (ref null ...).
            if (!node.IsForm("ref"))
                throw new FormatException($"line {node.Token.Line}: expected (ref …) form, got {node.Head}");
            int i = 1;
            bool nullable = false;
            if (i < node.Children.Count
                && node.Children[i].Kind == SExprKind.Atom
                && node.Children[i].Token.Kind == TokenKind.Keyword
                && node.Children[i].AtomText() == "null")
            {
                nullable = true;
                i++;
            }
            if (i >= node.Children.Count)
                throw new FormatException($"line {node.Token.Line}: (ref …) missing heap type");
            var ht = ParseHeapType(ctx, node.Children[i]);
            i++;
            ExpectConsumed(node, i, "ref");

            // Apply ref+sign bits. For abstract heap types the bits are
            // already baked into the ValType constant (they were built with
            // ValType.NullableRef | SignBit). We need to swap the nullable
            // bit based on parsed nullability, and for typeidx defs we need
            // to add Ref (and optionally Nullable) on top of the raw index.
            if (ht.IsDefType())
            {
                // Raw typeidx — set Ref/NullableRef bits.
                var bits = nullable ? ValType.NullableRef : ValType.Ref;
                return (ValType)((int)ht | (int)bits);
            }
            // Abstract — the ValType constants already carry nullable+ref+sign
            // for the standard spelling. Toggle nullability via bitmask.
            return nullable ? ht.AsNullable() : ht.AsNonNullable();
        }

        private static int ResolveTypeIdx(TextParseContext ctx, SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected type index");
            var text = atom.AtomText();
            if (atom.Token.Kind == TokenKind.Id)
            {
                if (!ctx.Types.TryResolve(text, out var idx))
                    throw new FormatException($"line {atom.Token.Line}: unknown type {text}");
                return idx;
            }
            // Plain numeric — reserved token since it's not keyword / id.
            if (atom.Token.Kind != TokenKind.Reserved)
                throw new FormatException($"line {atom.Token.Line}: expected type index, got {atom}");
            if (!uint.TryParse(text, out var n))
                throw new FormatException($"line {atom.Token.Line}: bad type index '{text}'");
            return (int)n;
        }

        // ---- (param ...) / (result ...) -----------------------------------

        /// <summary>
        /// Parse a (param …) form at position <paramref name="index"/>. May
        /// be either <c>(param i32)</c> (anonymous, one type) or
        /// <c>(param $x i32)</c> (named, one type) or <c>(param i32 i64 …)</c>
        /// (anonymous run). Accumulates the types into <paramref name="out"/>.
        /// </summary>
        private static void ParseParamForm(TextParseContext ctx, SExpr form, List<ValType> outTypes)
        {
            int i = 1;
            // A named param has exactly one type — skip the id if present.
            if (i < form.Children.Count
                && form.Children[i].Kind == SExprKind.Atom
                && form.Children[i].Token.Kind == TokenKind.Id)
            {
                // Phase 1.3 doesn't thread local-names into a debug table,
                // but if we wanted to we'd do it here. Skipped for now.
                i++;
                if (i >= form.Children.Count)
                    throw new FormatException($"line {form.Token.Line}: (param $id …) missing type");
                outTypes.Add(ParseValType(ctx, form.Children[i]));
                i++;
                ExpectConsumed(form, i, "param");
                return;
            }
            // Anonymous run: every remaining child is a valtype.
            for (; i < form.Children.Count; i++)
                outTypes.Add(ParseValType(ctx, form.Children[i]));
        }

        private static void ParseResultForm(TextParseContext ctx, SExpr form, List<ValType> outTypes)
        {
            for (int i = 1; i < form.Children.Count; i++)
                outTypes.Add(ParseValType(ctx, form.Children[i]));
        }

        /// <summary>
        /// Parse a (func (param …)* (result …)*) signature body, starting at
        /// <paramref name="index"/> in the given parent form. Returns the
        /// built <see cref="FunctionType"/>.
        /// </summary>
        internal static FunctionType ParseFuncTypeSignature(TextParseContext ctx, SExpr parent, ref int index)
        {
            var parameters = new List<ValType>();
            var results = new List<ValType>();
            while (index < parent.Children.Count)
            {
                var child = parent.Children[index];
                if (child.Kind != SExprKind.List) break;
                if (child.IsForm("param"))
                {
                    ParseParamForm(ctx, child, parameters);
                    index++;
                    continue;
                }
                if (child.IsForm("result"))
                {
                    ParseResultForm(ctx, child, results);
                    index++;
                    continue;
                }
                break;
            }
            return new FunctionType(
                parameters.Count == 0 ? ResultType.Empty : new ResultType(parameters.ToArray()),
                results.Count == 0 ? ResultType.Empty : new ResultType(results.ToArray()));
        }

        // ---- (type ...) form ----------------------------------------------

        /// <summary>
        /// Parse a <c>(type $id? (func signature))</c> form.
        /// Also accepts <c>(type (sub final? (func …)))</c> and struct/array
        /// variants — but the GC subtype forms beyond a plain <c>(func …)</c>
        /// body are left for a later extension.
        /// </summary>
        private static void ParseTypeForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            var name = TryReadIdAt(form, ref i);
            if (i >= form.Children.Count)
                throw new FormatException($"line {form.Token.Line}: (type …) missing body");

            var body = form.Children[i];
            i++;
            ExpectConsumed(form, i, "type");

            FunctionType ft;
            if (body.IsForm("func"))
            {
                int bi = 1;
                ft = ParseFuncTypeSignature(ctx, body, ref bi);
                ExpectConsumed(body, bi, "func");
            }
            else
            {
                // Struct / array / sub — not covered in phase 1.3. Emit a
                // stub empty FunctionType so the index slot stays consistent,
                // and throw a clear error so it's not silently accepted. This
                // gives a single choke point for GC-types work in phase 1.5+.
                throw new NotSupportedException(
                    $"line {body.Token.Line}: (type …) body '{body.Head}' not yet supported in phase 1.3 (func only)");
            }

            // Bind the $name → index, then append to Module.Types as a single
            // non-recursive SubType (no supertypes, final).
            ctx.Types.Declare(name);
            var sub = new SubType(ft, final: true);
            ctx.Module.Types.Add(new RecursiveType(sub));
        }

        // ---- Type-use ------------------------------------------------------
        //
        // A "typeuse" in a func / tag / block-type slot may be spelled in
        // three ways:
        //   (type $n)                             — reference an existing type
        //   (type $n) (param …)* (result …)*      — reference + redundant check
        //   (param …)* (result …)*                — implicit; synthesize type
        //
        // Phase 1.3 only handles the first two. The third is folded into
        // Phase 1.4 as part of function-signature parsing (needs bidirectional
        // dedup with Module.Types).

        /// <summary>
        /// Parse a typeuse that REQUIRES an explicit <c>(type $n)</c>. Returns
        /// the resolved type index. Used by Phase 1.3 imports / tags that
        /// need a ready type at parse time.
        /// </summary>
        internal static int ParseExplicitTypeUse(TextParseContext ctx, SExpr parent, ref int index)
        {
            if (index >= parent.Children.Count)
                throw new FormatException($"line {parent.Token.Line}: expected (type …)");
            var child = parent.Children[index];
            if (!child.IsForm("type"))
                throw new FormatException($"line {child.Token.Line}: expected (type …), got {child.Head}");
            if (child.Children.Count != 2)
                throw new FormatException($"line {child.Token.Line}: (type …) must have exactly one operand");
            int idx = ResolveTypeIdx(ctx, child.Children[1]);
            index++;
            return idx;
        }
    }
}
