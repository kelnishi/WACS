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
            if (atom.Token.Kind != TokenKind.Reserved)
                throw new FormatException($"line {atom.Token.Line}: expected type index, got {atom}");
            return (int)(uint)System.Convert.ToInt64(
                text.Replace("_", "").StartsWith("0x") || text.Replace("_", "").StartsWith("0X")
                    ? System.Convert.ToUInt32(text.Replace("_", "").Substring(2), 16)
                    : System.Convert.ToUInt32(text.Replace("_", ""), 10));
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
        /// Parse a <c>(type $id? (func|struct|array|sub body))</c> form.
        /// All GC composite types are first-class; the body is stored in
        /// the appropriate <see cref="CompositeType"/> subclass
        /// (FunctionType / StructType / ArrayType).
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

            var sub = ParseSubType(ctx, body);
            ctx.Types.Declare(name);
            ctx.Module.Types.Add(new RecursiveType(sub));
            ctx.TypesFromRec.Add(false);
        }

        /// <summary>
        /// Parse a <c>(rec (type …) (type …) …)</c> recursive type group.
        /// All inner types are collected into a single
        /// <see cref="RecursiveType"/> with multiple subtypes — matches
        /// the binary encoder, which emits a rec group as one entry in
        /// the type section containing all subtypes.
        /// </summary>
        private static void ParseRecTypeForm(TextParseContext ctx, SExpr form)
        {
            var subs = new List<SubType>();
            for (int i = 1; i < form.Children.Count; i++)
            {
                var inner = form.Children[i];
                if (inner.Kind != SExprKind.List || !inner.IsForm("type"))
                    throw new FormatException(
                        $"line {inner.Token.Line}: (rec …) expects (type …) children");

                int bi = 1;
                var subName = TryReadIdAt(inner, ref bi);
                if (bi >= inner.Children.Count)
                    throw new FormatException($"line {inner.Token.Line}: (type …) missing body");
                var body = inner.Children[bi];
                var sub = ParseSubType(ctx, body);

                ctx.Types.Declare(subName);
                subs.Add(sub);
            }
            ctx.Module.Types.Add(new RecursiveType(subs.ToArray()));
            ctx.TypesFromRec.Add(true);
        }

        /// <summary>
        /// Parse a SubType body: either a bare composite (<c>func</c>,
        /// <c>struct</c>, <c>array</c>) or a <c>(sub final? super* body)</c>
        /// wrapper.
        /// </summary>
        private static SubType ParseSubType(TextParseContext ctx, SExpr body)
        {
            if (body.IsForm("sub"))
            {
                // (sub final? $super* body)
                int si = 1;
                bool final = false;
                if (si < body.Children.Count
                    && body.Children[si].Kind == SExprKind.Atom
                    && body.Children[si].Token.Kind == TokenKind.Keyword
                    && body.Children[si].AtomText() == "final")
                {
                    final = true;
                    si++;
                }
                var supers = new List<TypeIdx>();
                while (si < body.Children.Count)
                {
                    var ch = body.Children[si];
                    if (ch.Kind == SExprKind.Atom)
                    {
                        supers.Add((TypeIdx)ResolveTypeIdx(ctx, ch));
                        si++;
                        continue;
                    }
                    break;
                }
                if (si >= body.Children.Count)
                    throw new FormatException($"line {body.Token.Line}: (sub …) missing composite body");
                var inner = body.Children[si++];
                ExpectConsumed(body, si, "sub");
                var comp = ParseCompositeType(ctx, inner);
                return new SubType(supers.ToArray(), comp, final);
            }
            var composite = ParseCompositeType(ctx, body);
            return new SubType(composite, final: true);
        }

        private static CompositeType ParseCompositeType(TextParseContext ctx, SExpr body)
        {
            if (body.IsForm("func"))
            {
                int bi = 1;
                var ft = ParseFuncTypeSignature(ctx, body, ref bi);
                ExpectConsumed(body, bi, "func");
                return ft;
            }
            if (body.IsForm("struct"))
            {
                var fields = new List<FieldType>();
                for (int k = 1; k < body.Children.Count; k++)
                {
                    var f = body.Children[k];
                    if (f.Kind != SExprKind.List || !f.IsForm("field"))
                        throw new FormatException($"line {f.Token.Line}: (struct …) expects (field …) children");
                    foreach (var ft in ParseFieldForm(ctx, f))
                        fields.Add(ft);
                }
                return new StructType(fields.ToArray());
            }
            if (body.IsForm("array"))
            {
                if (body.Children.Count != 2)
                    throw new FormatException($"line {body.Token.Line}: (array …) expects one field");
                var inner = body.Children[1];
                if (inner.Kind != SExprKind.List || !inner.IsForm("field"))
                {
                    // (array T)  or  (array (mut T))  — treat as implicit single field
                    var single = ParseFieldNoWrapper(ctx, inner);
                    return new ArrayType(single);
                }
                var fields = ParseFieldForm(ctx, inner);
                if (fields.Count != 1)
                    throw new FormatException($"line {body.Token.Line}: (array …) expects exactly one field");
                return new ArrayType(fields[0]);
            }
            throw new FormatException(
                $"line {body.Token.Line}: (type …) body '{body.Head}' not recognized");
        }

        /// <summary>
        /// Parse one <c>(field …)</c> form. A single field form can
        /// contain multiple storage types (each spawning a separate
        /// FieldType), or a named form with a single type.
        /// Accepted shapes:
        ///   (field T+)              anonymous, one or more types
        ///   (field $name T)         named, single type
        ///   (field (mut T))         anonymous mutable
        ///   (field $name (mut T))   named mutable
        /// </summary>
        private static List<FieldType> ParseFieldForm(TextParseContext ctx, SExpr form)
        {
            var list = new List<FieldType>();
            int i = 1;
            // Named field: (field $name storage)
            if (i < form.Children.Count
                && form.Children[i].Kind == SExprKind.Atom
                && form.Children[i].Token.Kind == TokenKind.Id)
            {
                i++;
                if (i >= form.Children.Count)
                    throw new FormatException($"line {form.Token.Line}: (field $name …) missing type");
                list.Add(ParseFieldNoWrapper(ctx, form.Children[i]));
                i++;
                if (i != form.Children.Count)
                    throw new FormatException($"line {form.Token.Line}: named (field $name …) expects exactly one type");
                return list;
            }
            // Anonymous: one or more types, each becomes a field.
            for (; i < form.Children.Count; i++)
                list.Add(ParseFieldNoWrapper(ctx, form.Children[i]));
            return list;
        }

        /// <summary>
        /// Parse a single field storage type + mutability, not wrapped
        /// in a <c>(field …)</c> form.
        /// </summary>
        private static FieldType ParseFieldNoWrapper(TextParseContext ctx, SExpr node)
        {
            if (node.Kind == SExprKind.List && node.IsForm("mut"))
            {
                if (node.Children.Count != 2)
                    throw new FormatException($"line {node.Token.Line}: (mut …) takes one type");
                var vt = ParseStorageType(ctx, node.Children[1]);
                return new FieldType(vt, Mutability.Mutable);
            }
            var storage = ParseStorageType(ctx, node);
            return new FieldType(storage, Mutability.Immutable);
        }

        /// <summary>
        /// Parse a storage type. Extends <see cref="ParseValType"/> to
        /// accept the packed types <c>i8</c> and <c>i16</c> which are
        /// valid only in GC struct/array fields.
        /// </summary>
        private static ValType ParseStorageType(TextParseContext ctx, SExpr node)
        {
            if (node.Kind == SExprKind.Atom && node.Token.Kind == TokenKind.Keyword)
            {
                switch (node.AtomText())
                {
                    case "i8":  return ValType.I8;
                    case "i16": return ValType.I16;
                }
            }
            return ParseValType(ctx, node);
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
