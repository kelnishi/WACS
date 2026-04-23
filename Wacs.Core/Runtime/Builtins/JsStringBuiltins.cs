// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Text;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime.Builtins
{
    /// <summary>
    /// Implements the WebAssembly 3.0 JS String Builtins proposal
    /// (https://github.com/WebAssembly/js-string-builtins) against .NET
    /// System.String. Wasm modules that import from the "wasm:js-string"
    /// namespace get optimized native string ops without having to marshal
    /// through linear memory.
    ///
    /// The proposal is JS-flavored in name only — every operation is defined
    /// against UTF-16 code units, which matches System.String exactly, so a
    /// straightforward environment swap yields observably identical behavior.
    ///
    /// Host opt-in is explicit: call <see cref="BindTo"/> on a WasmRuntime
    /// before instantiating a module that imports from "wasm:js-string".
    /// Modules that don't import from this namespace are unaffected and pay
    /// no cost.
    ///
    /// Import signatures follow the proposal's reference test fixtures at
    /// https://github.com/WebAssembly/js-string-builtins/blob/main/proposals/
    /// js-string-builtins/Overview.md.
    /// </summary>
    public static class JsStringBuiltins
    {
        public const string ModuleName = "wasm:js-string";

        /// <summary>
        /// Register all 13 builtins under the "wasm:js-string" namespace.
        /// Must be called before <c>InstantiateModule</c> for modules that
        /// import from the namespace.
        /// </summary>
        public static void BindTo(WasmRuntime runtime)
        {
            // 11 simple externref / i32 functions.
            Bind(runtime, "test", InParams(ValType.ExternRef), I32, Test);
            Bind(runtime, "cast", InParams(ValType.ExternRef), NonNullExtern, Cast);
            Bind(runtime, "length", InParams(ValType.ExternRef), I32, Length);
            Bind(runtime, "concat", InParams(ValType.ExternRef, ValType.ExternRef), NonNullExtern, Concat);
            Bind(runtime, "substring", InParams(ValType.ExternRef, ValType.I32, ValType.I32), NonNullExtern, Substring);
            Bind(runtime, "equals", InParams(ValType.ExternRef, ValType.ExternRef), I32, Equals);
            Bind(runtime, "compare", InParams(ValType.ExternRef, ValType.ExternRef), I32, Compare);
            Bind(runtime, "charCodeAt", InParams(ValType.ExternRef, ValType.I32), I32, CharCodeAt);
            Bind(runtime, "codePointAt", InParams(ValType.ExternRef, ValType.I32), I32, CodePointAt);
            Bind(runtime, "fromCharCode", InParams(ValType.I32), NonNullExtern, FromCharCode);
            Bind(runtime, "fromCodePoint", InParams(ValType.I32), NonNullExtern, FromCodePoint);

            // 2 GC-array-typed functions. We accept the generic arrayref
            // supertype; contravariant import matching lets modules that
            // declare (ref null (array (mut i16))) pass their specific type
            // in. Validated structurally at runtime (element must unpack to
            // a 16-bit code unit).
            Bind(runtime, "fromCharCodeArray",
                InParams(ValType.Array, ValType.I32, ValType.I32), NonNullExtern, FromCharCodeArray);
            Bind(runtime, "intoCharCodeArray",
                InParams(ValType.ExternRef, ValType.Array, ValType.I32), I32, IntoCharCodeArray);
        }

        // ---- Convenience type constructors ---------------------------------

        private static readonly ResultType I32 = new(ValType.I32);
        private static readonly ResultType NonNullExtern = new(ValType.Extern);

        private static ResultType InParams(params ValType[] types) => new(types);

        private static void Bind(WasmRuntime runtime, string name,
            ResultType inTypes, ResultType outTypes, Action<ExecContext> impl)
        {
            var type = new FunctionType(inTypes, outTypes);
            runtime.BindHostFunction((ModuleName, name),
                new JsStringBuiltinFunc(name, type, impl));
        }

        // ---- Builtin implementations --------------------------------------

        // Pop the top-of-stack externref and return the underlying string.
        // Traps on null / non-string, matching spec §4.2: only `test` is
        // null-tolerant.
        private static string PopString(ExecContext ctx, string op)
        {
            var v = ctx.OpStack.PopRefType();
            if (v.IsNullRef)
                throw new TrapException($"js-string.{op}: null reference");
            if (v.GcRef is not JsStringRef jsRef)
                throw new TrapException($"js-string.{op}: operand is not a js string");
            return jsRef.Value;
        }

        private static void PushString(ExecContext ctx, string s)
        {
            ctx.OpStack.PushValue(new Value(ValType.Extern, 0L, new JsStringRef(s)));
        }

        // test(externref) → i32 — 1 if the ref is a js-string, 0 otherwise
        // (including null). The only builtin that does not trap on wrong type.
        private static void Test(ExecContext ctx)
        {
            var v = ctx.OpStack.PopRefType();
            int result = (!v.IsNullRef && v.GcRef is JsStringRef) ? 1 : 0;
            ctx.OpStack.PushValue(new Value(result));
        }

        // cast(externref) → (ref extern) — assert-and-return. Traps on null
        // or non-string; pushes the same ref typed as non-null.
        private static void Cast(ExecContext ctx)
        {
            var s = PopString(ctx, "cast");
            PushString(ctx, s);
        }

        // length(externref) → i32 — UTF-16 code-unit count.
        private static void Length(ExecContext ctx)
        {
            var s = PopString(ctx, "length");
            ctx.OpStack.PushValue(new Value(s.Length));
        }

        // concat(externref, externref) → (ref extern)
        private static void Concat(ExecContext ctx)
        {
            var b = PopString(ctx, "concat");
            var a = PopString(ctx, "concat");
            PushString(ctx, string.Concat(a, b));
        }

        // substring(externref, i32 start, i32 end) → (ref extern)
        // Per spec §4.2: start/end clamped to [0, length]; returns empty if
        // end <= start after clamping.
        private static void Substring(ExecContext ctx)
        {
            int end = ctx.OpStack.PopI32();
            int start = ctx.OpStack.PopI32();
            var s = PopString(ctx, "substring");

            int len = s.Length;
            int clampedStart = Math.Max(0, Math.Min(start, len));
            int clampedEnd = Math.Max(0, Math.Min(end, len));
            if (clampedEnd <= clampedStart)
            {
                PushString(ctx, string.Empty);
                return;
            }
            PushString(ctx, s.Substring(clampedStart, clampedEnd - clampedStart));
        }

        // equals(externref, externref) → i32 — ordinal value comparison.
        // Per spec: null-null → 1, null-any → 0. Type-traps if a non-null
        // operand isn't a string.
        private static void Equals(ExecContext ctx)
        {
            var b = ctx.OpStack.PopRefType();
            var a = ctx.OpStack.PopRefType();

            if (a.IsNullRef && b.IsNullRef)
            {
                ctx.OpStack.PushValue(new Value(1));
                return;
            }
            if (a.IsNullRef || b.IsNullRef)
            {
                ctx.OpStack.PushValue(new Value(0));
                return;
            }
            if (a.GcRef is not JsStringRef ra || b.GcRef is not JsStringRef rb)
                throw new TrapException("js-string.equals: operand is not a js string");

            ctx.OpStack.PushValue(new Value(string.Equals(ra.Value, rb.Value, StringComparison.Ordinal) ? 1 : 0));
        }

        // compare(externref, externref) → i32 — ordinal, normalized to {-1,0,1}.
        // Traps on null on either side (only equals allows null).
        private static void Compare(ExecContext ctx)
        {
            var b = PopString(ctx, "compare");
            var a = PopString(ctx, "compare");
            int cmp = string.CompareOrdinal(a, b);
            ctx.OpStack.PushValue(new Value(cmp < 0 ? -1 : cmp > 0 ? 1 : 0));
        }

        // charCodeAt(externref, i32) → i32 — UTF-16 code unit or -1 if OOB.
        private static void CharCodeAt(ExecContext ctx)
        {
            int idx = ctx.OpStack.PopI32();
            var s = PopString(ctx, "charCodeAt");
            int result = (uint)idx < (uint)s.Length ? s[idx] : -1;
            ctx.OpStack.PushValue(new Value(result));
        }

        // codePointAt(externref, i32) → i32 — full codepoint (handles surrogate
        // pairs), or -1 if OOB. Lone surrogates return as-is per spec.
        private static void CodePointAt(ExecContext ctx)
        {
            int idx = ctx.OpStack.PopI32();
            var s = PopString(ctx, "codePointAt");

            if ((uint)idx >= (uint)s.Length)
            {
                ctx.OpStack.PushValue(new Value(-1));
                return;
            }

            char c = s[idx];
            if (char.IsHighSurrogate(c) && idx + 1 < s.Length)
            {
                char low = s[idx + 1];
                if (char.IsLowSurrogate(low))
                {
                    int cp = char.ConvertToUtf32(c, low);
                    ctx.OpStack.PushValue(new Value(cp));
                    return;
                }
            }
            ctx.OpStack.PushValue(new Value((int)c));
        }

        // fromCharCode(i32) → (ref extern) — single UTF-16 code unit to string.
        // Low 16 bits used; high bits ignored per spec.
        private static void FromCharCode(ExecContext ctx)
        {
            int code = ctx.OpStack.PopI32();
            PushString(ctx, new string((char)(ushort)code, 1));
        }

        // fromCodePoint(i32) → (ref extern) — single codepoint to string.
        // Traps on code > 0x10FFFF or < 0 (matches JS RangeError).
        private static void FromCodePoint(ExecContext ctx)
        {
            int cp = ctx.OpStack.PopI32();
            if (cp < 0 || cp > 0x10FFFF)
                throw new TrapException("js-string.fromCodePoint: code point out of range");
            PushString(ctx, char.ConvertFromUtf32(cp));
        }

        // fromCharCodeArray(arrayref, i32 start, i32 end) → (ref extern)
        // Reads elements [start, end) as UTF-16 code units and builds a string.
        // Traps on null array or OOB indices.
        private static void FromCharCodeArray(ExecContext ctx)
        {
            int end = ctx.OpStack.PopI32();
            int start = ctx.OpStack.PopI32();
            var arrVal = ctx.OpStack.PopRefType();

            if (arrVal.IsNullRef)
                throw new TrapException("js-string.fromCharCodeArray: null array");
            if (arrVal.GcRef is not StoreArray arr)
                throw new TrapException("js-string.fromCharCodeArray: operand is not an array");
            if (start < 0 || end < start || end > arr.Length)
                throw new TrapException("js-string.fromCharCodeArray: range out of bounds");

            int len = end - start;
            if (len == 0)
            {
                PushString(ctx, string.Empty);
                return;
            }
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append((char)(ushort)arr[start + i].Data.Int32);
            PushString(ctx, sb.ToString());
        }

        // intoCharCodeArray(externref, arrayref, i32 start) → i32
        // Copies the string's UTF-16 code units into arr[start..start+len].
        // Returns count of code units written. Traps on null array or OOB.
        private static void IntoCharCodeArray(ExecContext ctx)
        {
            int start = ctx.OpStack.PopI32();
            var arrVal = ctx.OpStack.PopRefType();
            var s = PopString(ctx, "intoCharCodeArray");

            if (arrVal.IsNullRef)
                throw new TrapException("js-string.intoCharCodeArray: null array");
            if (arrVal.GcRef is not StoreArray arr)
                throw new TrapException("js-string.intoCharCodeArray: operand is not an array");
            if (start < 0 || start + s.Length > arr.Length)
                throw new TrapException("js-string.intoCharCodeArray: range out of bounds");

            for (int i = 0; i < s.Length; i++)
                arr[start + i] = new Value(s[i]);
            ctx.OpStack.PushValue(new Value(s.Length));
        }
    }

    /// <summary>
    /// Minimal IFunctionInstance wrapper for a JS-string builtin. Goes
    /// straight to the OpStack rather than the delegate marshaler because
    /// the marshaler doesn't yet route externref / arrayref params.
    /// </summary>
    internal sealed class JsStringBuiltinFunc : IFunctionInstance
    {
        private readonly Action<ExecContext> _impl;

        public JsStringBuiltinFunc(string name, FunctionType type, Action<ExecContext> impl)
        {
            Name = name;
            Type = type;
            _impl = impl;
        }

        public FunctionType Type { get; }
        public string Name { get; }
        public string Id => $"{JsStringBuiltins.ModuleName}.{Name}";
        public bool IsAsync => false;

        public bool IsExport
        {
            get => true;
            set { /* builtins are inherently exported; setter is a no-op */ }
        }

        public void SetName(string name) { /* name is fixed at construction */ }

        public void Invoke(ExecContext context) => _impl(context);
    }
}
