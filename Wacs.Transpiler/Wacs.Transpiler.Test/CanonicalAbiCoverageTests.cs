// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Coverage matrix locking in the set of canonical-ABI shapes
    /// <see cref="DirectLinkedImportEmit.CanEmitDirect"/> accepts as
    /// of v1. Each row builds a synthetic <see cref="HostPackageResolver.Binding"/>
    /// + <see cref="FunctionType"/> for one CLR↔wasm shape and asserts
    /// the gate approves. If a future refactor narrows
    /// <c>CanonicalSlotCount</c> or tightens a wire-form check the
    /// matching row regresses to red.
    /// </summary>
    public class CanonicalAbiCoverageTests
    {
        // Free-function interface — methods cover every PARAM shape
        // and every primitive RETURN shape we want to pin. Aggregate
        // returns get their own surface to keep the wasm result-type
        // story clear per row.
        [WitSource(@"interface shapes",
            Package = "wacs:cov@1.0.0", Interface = "shapes")]
        public interface IShapes
        {
            // ---- Primitive params (1 slot each) -------------------
            [WitSource(@"p-bool: func(x: bool);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-bool")]
            void PBool(bool x);
            [WitSource(@"p-s8: func(x: s8);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-s8")]
            void PS8(sbyte x);
            [WitSource(@"p-u8: func(x: u8);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-u8")]
            void PU8(byte x);
            [WitSource(@"p-s16: func(x: s16);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-s16")]
            void PS16(short x);
            [WitSource(@"p-u16: func(x: u16);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-u16")]
            void PU16(ushort x);
            [WitSource(@"p-s32: func(x: s32);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-s32")]
            void PS32(int x);
            [WitSource(@"p-u32: func(x: u32);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-u32")]
            void PU32(uint x);
            [WitSource(@"p-s64: func(x: s64);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-s64")]
            void PS64(long x);
            [WitSource(@"p-u64: func(x: u64);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-u64")]
            void PU64(ulong x);
            [WitSource(@"p-f32: func(x: f32);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-f32")]
            void PF32(float x);
            [WitSource(@"p-f64: func(x: f64);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-f64")]
            void PF64(double x);
            [WitSource(@"p-enum: func(x: cover-enum);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-enum")]
            void PEnum(CoverEnum x);

            // ---- Variable-shape PARAMS (2 slots: ptr + len/count) -
            [WitSource(@"p-string: func(s: string);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-string")]
            void PString(string s);
            [WitSource(@"p-bytes: func(b: list<u8>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-bytes")]
            void PBytes(byte[] b);
            [WitSource(@"p-int-list: func(xs: list<s32>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-int-list")]
            void PIntList(int[] xs);
            [WitSource(@"p-string-list: func(xs: list<string>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-string-list")]
            void PStringList(string[] xs);
            [WitSource(@"p-byte-arr-list: func(xs: list<list<u8>>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-byte-arr-list")]
            void PByteArrList(byte[][] xs);

            // ---- Resource params --------------------------------
            [WitSource(@"p-resource: func(r: borrow<cov-res>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-resource")]
            void PResource(ICovRes r);
            [WitSource(@"p-resource-list: func(rs: list<borrow<cov-res>>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-resource-list")]
            void PResourceList(ICovRes[] rs);

            // ---- Compound params --------------------------------
            // tuple<s32, s32> → 2 i32
            [WitSource(@"p-tuple-ii: func(t: tuple<s32, s32>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-tuple-ii")]
            void PTupleII((int, int) t);
            // tuple<s32, string> → 1 i32 + 2 i32 = 3 i32
            [WitSource(@"p-tuple-is: func(t: tuple<s32, string>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-tuple-is")]
            void PTupleIS((int, string) t);
            // option<s32> → 1 disc + 1 value = 2 i32
            [WitSource(@"p-option-int: func(o: option<s32>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-option-int")]
            void POptionInt(Option<int> o);
            // option<string> → 1 disc + 2 i32 = 3 i32
            [WitSource(@"p-option-string: func(o: option<string>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-option-string")]
            void POptionString(Option<string> o);
            // result<s32, s32> → 1 disc + 1 i32 = 2 i32 (joined-flat)
            [WitSource(@"p-result-ii: func(r: result<s32, s32>);",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "p-result-ii")]
            void PResultII(Result<int, int> r);

            // ---- Primitive RETURNS (1 wasm result, no retArea) ---
            [WitSource(@"r-s32: func() -> s32;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-s32")]
            int RS32();
            [WitSource(@"r-u32: func() -> u32;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-u32")]
            uint RU32();
            [WitSource(@"r-s64: func() -> s64;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-s64")]
            long RS64();
            [WitSource(@"r-u64: func() -> u64;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-u64")]
            ulong RU64();
            [WitSource(@"r-f32: func() -> f32;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-f32")]
            float RF32();
            [WitSource(@"r-f64: func() -> f64;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-f64")]
            double RF64();
            // own<R> return → 1 i32 (handle).
            [WitSource(@"r-resource: func() -> own<cov-res>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-resource")]
            ICovRes RResource();

            // ---- Aggregate RETURNS (0 wasm result + retArea i32) -
            [WitSource(@"r-string: func() -> string;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-string")]
            string RString();
            [WitSource(@"r-bytes: func() -> list<u8>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-bytes")]
            byte[] RBytes();
            [WitSource(@"r-int-list: func() -> list<s32>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-int-list")]
            int[] RIntList();
            [WitSource(@"r-string-list: func() -> list<string>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-string-list")]
            string[] RStringList();
            [WitSource(@"r-option-int: func() -> option<s32>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-option-int")]
            Option<int> ROptionInt();
            [WitSource(@"r-option-string: func() -> option<string>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-option-string")]
            Option<string> ROptionString();
            [WitSource(@"r-result-ii: func() -> result<s32, s32>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-result-ii")]
            Result<int, int> RResultII();
            [WitSource(@"r-tuple-is: func() -> tuple<s32, string>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-tuple-is")]
            (int, string) RTupleIS();
            [WitSource(@"r-resource-list: func() -> list<own<cov-res>>;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "r-resource-list")]
            ICovRes[] RResourceList();
        }

        // Trivial resource for the resource-typed shapes.
        [WitSource(@"resource cov-res { read: func() -> u32; }",
            Package = "wacs:cov@1.0.0", Interface = "shapes",
            Item = "cov-res")]
        public interface ICovRes
        {
            [WitSource(@"read: func() -> u32;",
                Package = "wacs:cov@1.0.0", Interface = "shapes",
                Item = "cov-res.read")]
            uint Read();
        }

        // Minimal resources type for shapes that demand resolver+
        // resources discovery (resource-array RETURN).
        public sealed class CovResources
        {
            public object GetResource(Type t, int h) =>
                throw new NotImplementedException();
            public int AllocateResource(Type t, object o) =>
                throw new NotImplementedException();
        }

        public enum CoverEnum : uint
        {
            A = 0, B = 1, C = 2,
        }

        // Cached resolvers keyed on resourcesType presence — most
        // shapes don't need the resources class, but a couple do
        // (e.g. list<own<R>> RETURN goes through AllocateResource).
        private static HostPackageResolver Resolver { get; } =
            HostPackageResolver.FromAssemblies(
                new[] { typeof(IShapes).Assembly });
        private static HostPackageResolver ResolverWithRes { get; } =
            HostPackageResolver.FromAssemblies(
                new[] { typeof(IShapes).Assembly },
                resourcesType: typeof(CovResources));

        private static ValType[] V(params ValType[] v) => v;

        // ============= PARAM-shape coverage =====================

        public static IEnumerable<object[]> ParamShapes()
        {
            // (methodName, expected wasm param wire-form,
            //  needsResourcesType)
            yield return new object[] { nameof(IShapes.PBool),       V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PS8),         V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PU8),         V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PS16),        V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PU16),        V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PS32),        V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PU32),        V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PS64),        V(ValType.I64),                                        false };
            yield return new object[] { nameof(IShapes.PU64),        V(ValType.I64),                                        false };
            yield return new object[] { nameof(IShapes.PF32),        V(ValType.F32),                                        false };
            yield return new object[] { nameof(IShapes.PF64),        V(ValType.F64),                                        false };
            yield return new object[] { nameof(IShapes.PEnum),       V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PString),     V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.PBytes),      V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.PIntList),    V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.PStringList), V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.PByteArrList),V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.PResource),   V(ValType.I32),                                        false };
            yield return new object[] { nameof(IShapes.PResourceList),V(ValType.I32, ValType.I32),                          false };
            yield return new object[] { nameof(IShapes.PTupleII),    V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.PTupleIS),    V(ValType.I32, ValType.I32, ValType.I32),              false };
            yield return new object[] { nameof(IShapes.POptionInt),  V(ValType.I32, ValType.I32),                           false };
            yield return new object[] { nameof(IShapes.POptionString),V(ValType.I32, ValType.I32, ValType.I32),             false };
            yield return new object[] { nameof(IShapes.PResultII),   V(ValType.I32, ValType.I32),                           false };
        }

        [Theory]
        [MemberData(nameof(ParamShapes))]
        public void Param_shape_passes_direct_link_gate(
            string methodName,
            ValType[] expectedParams,
            bool needsResourcesType)
        {
            var method = typeof(IShapes).GetMethod(methodName)!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: methodName,
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            var wasm = new FunctionType(
                new ResultType(expectedParams),
                ResultType.Empty);
            var resolver = needsResourcesType
                ? ResolverWithRes : Resolver;
            Assert.True(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, resolver),
                "CanEmitDirect rejected shape " + methodName
                + " — gate trace fires on stderr when "
                + "TranspilerDiagnostics.Enabled is true. v1 phase 1a "
                + "wired the gate names into the trace so a regression "
                + "here is debuggable without rebuilding.");
        }

        // ============= PRIMITIVE-return coverage ================

        public static IEnumerable<object[]> PrimitiveReturnShapes()
        {
            yield return new object[] { nameof(IShapes.RS32),   ValType.I32 };
            yield return new object[] { nameof(IShapes.RU32),   ValType.I32 };
            yield return new object[] { nameof(IShapes.RS64),   ValType.I64 };
            yield return new object[] { nameof(IShapes.RU64),   ValType.I64 };
            yield return new object[] { nameof(IShapes.RF32),   ValType.F32 };
            yield return new object[] { nameof(IShapes.RF64),   ValType.F64 };
            // own<R> return: wasm wire is i32 (handle); CLR is the
            // resource interface. Goes through the isOwnReturn branch.
            yield return new object[] { nameof(IShapes.RResource), ValType.I32 };
        }

        [Theory]
        [MemberData(nameof(PrimitiveReturnShapes))]
        public void Primitive_return_shape_passes_direct_link_gate(
            string methodName, ValType expectedResult)
        {
            var method = typeof(IShapes).GetMethod(methodName)!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: methodName,
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            var wasm = new FunctionType(
                ResultType.Empty,
                new ResultType(expectedResult));
            // own<R> return needs PreferredResourcesType to be set
            // (the IL emit allocates a handle from the returned
            // instance — see DirectLinkedImportEmit isOwnReturn).
            var resolver = method.ReturnType == typeof(ICovRes)
                ? ResolverWithRes : Resolver;
            Assert.True(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, resolver),
                "CanEmitDirect rejected primitive-return shape "
                + methodName);
        }

        // ============= AGGREGATE-return coverage ================

        public static IEnumerable<object[]> AggregateReturnShapes()
        {
            // All aggregate returns use retArea: wasm has 0 results
            // and the LAST wasm param is i32 (the retArea ptr). For
            // this matrix every method is parameterless on the CLR
            // side, so the single wasm param IS the retArea slot.
            yield return new object[] { nameof(IShapes.RString),       false };
            yield return new object[] { nameof(IShapes.RBytes),        false };
            yield return new object[] { nameof(IShapes.RIntList),      false };
            yield return new object[] { nameof(IShapes.RStringList),   false };
            yield return new object[] { nameof(IShapes.ROptionInt),    false };
            yield return new object[] { nameof(IShapes.ROptionString), false };
            yield return new object[] { nameof(IShapes.RResultII),     false };
            yield return new object[] { nameof(IShapes.RTupleIS),      false };
            // list<own<R>>: aggregate-return AND each element needs
            // an AllocateResource call → demands resources class.
            yield return new object[] { nameof(IShapes.RResourceList), true };
        }

        [Theory]
        [MemberData(nameof(AggregateReturnShapes))]
        public void Aggregate_return_shape_passes_direct_link_gate(
            string methodName, bool needsResourcesType)
        {
            var method = typeof(IShapes).GetMethod(methodName)!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: methodName,
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            // Aggregate-return wire: params = [I32 retArea ptr];
            // results = []. CanEmitDirect detects this via the
            // "wasmResults.Length == 0 && last wasm param is I32"
            // condition + IsAggregateReturnSupported check.
            var wasm = new FunctionType(
                new ResultType(new[] { ValType.I32 }),
                ResultType.Empty);
            var resolver = needsResourcesType
                ? ResolverWithRes : Resolver;
            Assert.True(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, resolver),
                "CanEmitDirect rejected aggregate-return shape "
                + methodName);
        }

        // ============= Negative regression locks =================
        // A handful of shapes we EXPECT to reject — captures the
        // currently-unsupported edges so an accidental loosening
        // (e.g. accepting void return when wasm declares a result)
        // shows up as a test flip rather than a runtime trap.

        [Fact]
        public void Reject_void_return_with_wasm_result()
        {
            // C# void return + wasm sig declaring an i32 result →
            // return-void-vs-wasm-result gate fires.
            var method = typeof(IShapes).GetMethod(nameof(IShapes.PBool))!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: "bad-void-return",
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            // Sig: (i32) → i32. CLR has void return → reject.
            var wasm = new FunctionType(
                new ResultType(new[] { ValType.I32 }),
                new ResultType(new[] { ValType.I32 }));
            Assert.False(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, Resolver));
        }

        [Fact]
        public void Reject_multi_result_signature()
        {
            // Wasm sig with >1 result is not yet supported — only
            // 0 (void / aggregate-via-retArea) or 1 result wire.
            var method = typeof(IShapes).GetMethod(nameof(IShapes.RS32))!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: "bad-multi-result",
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            var wasm = new FunctionType(
                ResultType.Empty,
                new ResultType(new[] { ValType.I32, ValType.I32 }));
            Assert.False(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, Resolver));
        }

        [Fact]
        public void Reject_param_slot_count_mismatch()
        {
            // C# `void PString(string s)` lowers to 2 i32 slots
            // (ptr, len). If wasm declares only 1 i32 param, the
            // param-arity gate fires.
            var method = typeof(IShapes).GetMethod(nameof(IShapes.PString))!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: "bad-arity",
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            var wasm = new FunctionType(
                new ResultType(new[] { ValType.I32 }),
                ResultType.Empty);
            Assert.False(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, Resolver));
        }

        [Fact]
        public void Reject_param_slot_type_mismatch()
        {
            // `void PF32(float)` expects an F32 wasm slot. If wasm
            // declares I32, param-slot-type-mismatch fires.
            var method = typeof(IShapes).GetMethod(nameof(IShapes.PF32))!;
            var binding = new HostPackageResolver.Binding(
                module: "wacs:cov/shapes@1.0.0",
                entity: "bad-slot-type",
                interfaceType: typeof(IShapes),
                method: method,
                resourceKind: null,
                resourceName: null);
            var wasm = new FunctionType(
                new ResultType(new[] { ValType.I32 }),
                ResultType.Empty);
            Assert.False(DirectLinkedImportEmit.CanEmitDirect(
                binding, wasm, Resolver));
        }
    }
}
