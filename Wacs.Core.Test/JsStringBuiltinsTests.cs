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

using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Builtins;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// End-to-end tests for the 11 simple (externref/i32) JS String Builtins
    /// (wasm:js-string namespace), backed by .NET System.String.
    ///
    /// The 2 GC-array-typed builtins (fromCharCodeArray, intoCharCodeArray)
    /// are implemented in <see cref="JsStringBuiltins"/> and covered by
    /// direct-invoke unit tests further down — the WAT parser doesn't yet
    /// support `array.new_fixed` / `array.new` (tracked as phase 1.4 scope),
    /// so we can't round-trip them through a text module.
    /// </summary>
    public class JsStringBuiltinsTests
    {
        // Module importing the 11 simple builtins and re-exporting thin
        // wrappers for test drivers. No GC array types involved.
        private const string TestModule = @"
            (module
              (import ""wasm:js-string"" ""test""
                (func $test (param externref) (result i32)))
              (import ""wasm:js-string"" ""cast""
                (func $cast (param externref) (result (ref extern))))
              (import ""wasm:js-string"" ""length""
                (func $length (param externref) (result i32)))
              (import ""wasm:js-string"" ""concat""
                (func $concat (param externref externref) (result (ref extern))))
              (import ""wasm:js-string"" ""substring""
                (func $substring (param externref i32 i32) (result (ref extern))))
              (import ""wasm:js-string"" ""equals""
                (func $equals (param externref externref) (result i32)))
              (import ""wasm:js-string"" ""compare""
                (func $compare (param externref externref) (result i32)))
              (import ""wasm:js-string"" ""charCodeAt""
                (func $charCodeAt (param externref i32) (result i32)))
              (import ""wasm:js-string"" ""codePointAt""
                (func $codePointAt (param externref i32) (result i32)))
              (import ""wasm:js-string"" ""fromCharCode""
                (func $fromCharCode (param i32) (result (ref extern))))
              (import ""wasm:js-string"" ""fromCodePoint""
                (func $fromCodePoint (param i32) (result (ref extern))))

              (func (export ""call_test"") (param externref) (result i32)
                local.get 0
                call $test)
              (func (export ""call_cast"") (param externref) (result externref)
                local.get 0
                call $cast)
              (func (export ""call_length"") (param externref) (result i32)
                local.get 0
                call $length)
              (func (export ""call_concat"") (param externref externref) (result externref)
                local.get 0
                local.get 1
                call $concat)
              (func (export ""call_substring"") (param externref i32 i32) (result externref)
                local.get 0
                local.get 1
                local.get 2
                call $substring)
              (func (export ""call_equals"") (param externref externref) (result i32)
                local.get 0
                local.get 1
                call $equals)
              (func (export ""call_compare"") (param externref externref) (result i32)
                local.get 0
                local.get 1
                call $compare)
              (func (export ""call_charCodeAt"") (param externref i32) (result i32)
                local.get 0
                local.get 1
                call $charCodeAt)
              (func (export ""call_codePointAt"") (param externref i32) (result i32)
                local.get 0
                local.get 1
                call $codePointAt)
              (func (export ""call_fromCharCode"") (param i32) (result externref)
                local.get 0
                call $fromCharCode)
              (func (export ""call_fromCodePoint"") (param i32) (result externref)
                local.get 0
                call $fromCodePoint)
            )
        ";

        private static (WasmRuntime runtime, ModuleInstance inst) Build()
        {
            var runtime = new WasmRuntime();
            JsStringBuiltins.BindTo(runtime);
            var module = TextModuleParser.ParseWat(TestModule);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            return (runtime, inst);
        }

        private static Value Extern(string s) =>
            new(ValType.Extern, 0L, new JsStringRef(s));

        private static Value NullExtern() => Value.NullExternRef;

        private static Value[] Invoke(WasmRuntime runtime, string name, params Value[] args)
        {
            var addr = runtime.GetExportedFunction(("M", name));
            var inv = runtime.CreateStackInvoker(addr);
            return inv(args);
        }

        private static int InvokeI32(WasmRuntime runtime, string name, params Value[] args) =>
            (int)Invoke(runtime, name, args)[0];

        private static string InvokeString(WasmRuntime runtime, string name, params Value[] args)
        {
            var result = Invoke(runtime, name, args)[0];
            var jsRef = Assert.IsType<JsStringRef>(result.GcRef);
            return jsRef.Value;
        }

        // ---- test / cast ----------------------------------------------------

        [Fact]
        public void Test_Returns1ForJsString()
        {
            var (runtime, _) = Build();
            Assert.Equal(1, InvokeI32(runtime, "call_test", Extern("hi")));
        }

        [Fact]
        public void Test_Returns0ForNull()
        {
            var (runtime, _) = Build();
            Assert.Equal(0, InvokeI32(runtime, "call_test", NullExtern()));
        }

        [Fact]
        public void Cast_TrapsOnNonString()
        {
            var (runtime, _) = Build();
            Assert.Throws<TrapException>(() => InvokeI32(runtime, "call_cast", NullExtern()));
        }

        [Fact]
        public void Cast_PassesThroughJsString()
        {
            var (runtime, _) = Build();
            Assert.Equal("hello", InvokeString(runtime, "call_cast", Extern("hello")));
        }

        // ---- length ---------------------------------------------------------

        [Fact]
        public void Length_ReturnsUtf16CodeUnitCount()
        {
            var (runtime, _) = Build();
            Assert.Equal(5, InvokeI32(runtime, "call_length", Extern("hello")));
            Assert.Equal(0, InvokeI32(runtime, "call_length", Extern("")));
            // Astral: 2 UTF-16 code units
            Assert.Equal(2, InvokeI32(runtime, "call_length", Extern("😀")));
        }

        [Fact]
        public void Length_TrapsOnNull()
        {
            var (runtime, _) = Build();
            Assert.Throws<TrapException>(() => InvokeI32(runtime, "call_length", NullExtern()));
        }

        // ---- concat ---------------------------------------------------------

        [Fact]
        public void Concat_JoinsStrings()
        {
            var (runtime, _) = Build();
            Assert.Equal("foobar", InvokeString(runtime, "call_concat", Extern("foo"), Extern("bar")));
            Assert.Equal("abc", InvokeString(runtime, "call_concat", Extern(""), Extern("abc")));
        }

        // ---- substring ------------------------------------------------------

        [Theory]
        [InlineData("hello", 0, 5, "hello")]
        [InlineData("hello", 1, 4, "ell")]
        [InlineData("hello", 2, 2, "")] // empty
        [InlineData("hello", 3, 1, "")] // end <= start after clamp
        [InlineData("hello", -5, 3, "hel")] // clamped start to 0
        [InlineData("hello", 2, 999, "llo")] // clamped end to length
        public void Substring_ClampsAndReturns(string input, int start, int end, string expected)
        {
            var (runtime, _) = Build();
            Assert.Equal(expected, InvokeString(runtime, "call_substring",
                Extern(input), new Value(start), new Value(end)));
        }

        // ---- equals ---------------------------------------------------------

        [Fact]
        public void Equals_Ordinal()
        {
            var (runtime, _) = Build();
            Assert.Equal(1, InvokeI32(runtime, "call_equals", Extern("abc"), Extern("abc")));
            Assert.Equal(0, InvokeI32(runtime, "call_equals", Extern("abc"), Extern("ABC")));
        }

        [Fact]
        public void Equals_AllowsNulls()
        {
            var (runtime, _) = Build();
            Assert.Equal(1, InvokeI32(runtime, "call_equals", NullExtern(), NullExtern()));
            Assert.Equal(0, InvokeI32(runtime, "call_equals", NullExtern(), Extern("x")));
            Assert.Equal(0, InvokeI32(runtime, "call_equals", Extern("x"), NullExtern()));
        }

        // ---- compare --------------------------------------------------------

        [Fact]
        public void Compare_Ordinal()
        {
            var (runtime, _) = Build();
            Assert.Equal(0, InvokeI32(runtime, "call_compare", Extern("abc"), Extern("abc")));
            Assert.Equal(-1, InvokeI32(runtime, "call_compare", Extern("abc"), Extern("abd")));
            Assert.Equal(1, InvokeI32(runtime, "call_compare", Extern("abd"), Extern("abc")));
        }

        // ---- charCodeAt / codePointAt --------------------------------------

        [Theory]
        [InlineData("abc", 0, 0x61)]
        [InlineData("abc", 2, 0x63)]
        [InlineData("abc", 99, -1)] // OOB → -1
        [InlineData("abc", -1, -1)] // negative → -1 (unsigned comparison)
        public void CharCodeAt_ReturnsCodeUnitOrSentinel(string s, int idx, int expected)
        {
            var (runtime, _) = Build();
            Assert.Equal(expected, InvokeI32(runtime, "call_charCodeAt", Extern(s), new Value(idx)));
        }

        [Fact]
        public void CodePointAt_DecodesSurrogatePairs()
        {
            var (runtime, _) = Build();
            // "😀" encodes as U+D83D U+DE00 (code point U+1F600)
            Assert.Equal(0x1F600, InvokeI32(runtime, "call_codePointAt",
                Extern("😀"), new Value(0)));
            // Index into low surrogate → returns the lone surrogate code unit
            Assert.Equal(0xDE00, InvokeI32(runtime, "call_codePointAt",
                Extern("😀"), new Value(1)));
            // OOB
            Assert.Equal(-1, InvokeI32(runtime, "call_codePointAt",
                Extern("abc"), new Value(99)));
        }

        [Fact]
        public void CharCodeAt_RoundTripsLoneSurrogate()
        {
            // Spec: unpaired surrogates must survive unchanged — critical for
            // the "JS string" contract (and also what System.String does).
            var (runtime, _) = Build();
            Assert.Equal(0xD83D, InvokeI32(runtime, "call_charCodeAt",
                Extern("\uD83D"), new Value(0)));
        }

        // ---- fromCharCode / fromCodePoint ----------------------------------

        [Fact]
        public void FromCharCode_BuildsSingleCodeUnit()
        {
            var (runtime, _) = Build();
            Assert.Equal("A", InvokeString(runtime, "call_fromCharCode", new Value(0x41)));
            // High bits truncated
            Assert.Equal("A", InvokeString(runtime, "call_fromCharCode", new Value(0x10041)));
        }

        [Fact]
        public void FromCodePoint_BuildsBmpAndAstral()
        {
            var (runtime, _) = Build();
            Assert.Equal("A", InvokeString(runtime, "call_fromCodePoint", new Value(0x41)));
            Assert.Equal("😀", InvokeString(runtime, "call_fromCodePoint", new Value(0x1F600)));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0x110000)]
        public void FromCodePoint_TrapsOnOutOfRange(int cp)
        {
            var (runtime, _) = Build();
            Assert.Throws<TrapException>(() =>
                InvokeString(runtime, "call_fromCodePoint", new Value(cp)));
        }

        // ---- integration: chain calls across externref ---------------------

        [Fact]
        public void RoundTrip_FromCharCodeThenLength()
        {
            // Produce a string via fromCharCode, pipe it back into length.
            // The test walks the returned externref value through a second
            // wasm call — proves the GcRef survives the host↔wasm boundary.
            var (runtime, _) = Build();
            var built = Invoke(runtime, "call_fromCharCode", new Value('Z'))[0];
            Assert.Equal(1, InvokeI32(runtime, "call_length", built));
        }

        // ---- bind resolution -----------------------------------------------

        [Fact]
        public void BindTo_ModuleImportFailsWithoutBind()
        {
            // Sanity: if the host forgets to call BindTo, instantiation must
            // fail cleanly with an unbound-import error rather than a silent
            // mis-resolve.
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(TestModule);
            Assert.ThrowsAny<System.Exception>(() => runtime.InstantiateModule(module));
        }

        // ---- fromCharCodeArray / intoCharCodeArray (direct invoke) ---------
        //
        // These two builtins take a GC-typed (ref null (array (mut i16)))
        // parameter. WACS's text parser doesn't yet support `array.new` /
        // `array.new_fixed`, so we can't round-trip them through a WAT module.
        // Instead, drive the bound IFunctionInstance through CreateStackInvoker
        // with a hand-built StoreArray wrapped as a Value — same on-the-stack
        // encoding the GC instructions produce.

        private static Value MakeI16Array(int length, params int[] seed)
        {
            var arrType = new Wacs.Core.Types.ArrayType(
                new Wacs.Core.Types.FieldType(ValType.I16, Wacs.Core.Types.Mutability.Mutable));
            var arr = new StoreArray(default, arrType, new Value(ValType.I32, 0), length);
            for (int i = 0; i < seed.Length && i < length; i++)
                arr[i] = new Value(seed[i]);
            return new Value(ValType.Array, 0L, arr);
        }

        [Fact]
        public void FromCharCodeArray_ReadsRange()
        {
            var (runtime, _) = Build();
            var addr = runtime.GetExportedFunction((JsStringBuiltins.ModuleName, "fromCharCodeArray"));
            var inv = runtime.CreateStackInvoker(addr);

            var arr = MakeI16Array(5, 'H', 'e', 'l', 'l', 'o');
            var r = inv(new[] { arr, new Value(0), new Value(5) });
            Assert.Equal("Hello", Assert.IsType<JsStringRef>(r[0].GcRef).Value);
        }

        [Fact]
        public void FromCharCodeArray_Slice()
        {
            var (runtime, _) = Build();
            var addr = runtime.GetExportedFunction((JsStringBuiltins.ModuleName, "fromCharCodeArray"));
            var inv = runtime.CreateStackInvoker(addr);

            var arr = MakeI16Array(5, 'H', 'e', 'l', 'l', 'o');
            var r = inv(new[] { arr, new Value(1), new Value(4) });
            Assert.Equal("ell", Assert.IsType<JsStringRef>(r[0].GcRef).Value);
        }

        [Fact]
        public void FromCharCodeArray_TrapsOnNull()
        {
            var (runtime, _) = Build();
            var addr = runtime.GetExportedFunction((JsStringBuiltins.ModuleName, "fromCharCodeArray"));
            var inv = runtime.CreateStackInvoker(addr);

            var nullArr = new Value(ValType.Array);  // default → null
            Assert.Throws<TrapException>(() =>
                inv(new[] { nullArr, new Value(0), new Value(0) }));
        }

        [Fact]
        public void FromCharCodeArray_TrapsOnOutOfBounds()
        {
            var (runtime, _) = Build();
            var addr = runtime.GetExportedFunction((JsStringBuiltins.ModuleName, "fromCharCodeArray"));
            var inv = runtime.CreateStackInvoker(addr);

            var arr = MakeI16Array(3, 'a', 'b', 'c');
            Assert.Throws<TrapException>(() =>
                inv(new[] { arr, new Value(0), new Value(10) }));
        }

        [Fact]
        public void IntoCharCodeArray_WritesBack()
        {
            var (runtime, _) = Build();
            var addr = runtime.GetExportedFunction((JsStringBuiltins.ModuleName, "intoCharCodeArray"));
            var inv = runtime.CreateStackInvoker(addr);

            var arr = MakeI16Array(5);
            var r = inv(new[] { Extern("Hi!"), arr, new Value(0) });
            Assert.Equal(3, (int)r[0]);

            var storeArr = (StoreArray)arr.GcRef!;
            Assert.Equal('H', storeArr[0].Data.Int32);
            Assert.Equal('i', storeArr[1].Data.Int32);
            Assert.Equal('!', storeArr[2].Data.Int32);
        }

        [Fact]
        public void IntoCharCodeArray_TrapsOnInsufficientCapacity()
        {
            var (runtime, _) = Build();
            var addr = runtime.GetExportedFunction((JsStringBuiltins.ModuleName, "intoCharCodeArray"));
            var inv = runtime.CreateStackInvoker(addr);

            var arr = MakeI16Array(2);
            Assert.Throws<TrapException>(() =>
                inv(new[] { Extern("longer"), arr, new Value(0) }));
        }
    }
}
