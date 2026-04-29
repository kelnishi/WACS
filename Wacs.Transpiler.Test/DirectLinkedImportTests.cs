// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Runtime;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// End-to-end tests for the direct-linked WASI imports path.
    /// Each test transpiles a hand-crafted core wasm module that
    /// imports a single host function, supplies a host-package
    /// assembly carrying a <c>[WitSource]</c>-tagged interface that
    /// matches the import, and verifies that:
    ///
    /// 1. The transpiler resolves the import via
    ///    <see cref="HostPackageResolver"/>.
    /// 2. The generated module class accepts a bundle ctor param.
    /// 3. The emitted call-site IL bypasses the
    ///    <c>ImportDelegates</c> array entirely — the test plants a
    ///    stub <c>IImports</c> that <em>throws</em> if called, and
    ///    the export still returns the bundle's value cleanly.
    /// </summary>
    public class DirectLinkedImportTests
    {
        // ============== Test host-package surface ==============
        // PUBLIC so HostPackageResolver's GetExportedTypes() walk
        // sees them. The [WitSource] attribute is the contract — it
        // anchors the (Package, Interface) header that the resolver
        // rewrites into the wasm import wire-form module string.
        // The bundle is the typed aggregate that
        // ThinContext.HostBundle holds at runtime; the emitted IL
        // loads a property by interface-name convention (strip "I").

        [WitSource(@"interface env",
            Package = "my:test@1.0.0", Interface = "env")]
        public interface IEnv
        {
            [WitSource(@"get-value: func() -> u64;",
                Package = "my:test@1.0.0", Interface = "env",
                Item = "get-value")]
            ulong GetValue();

            // Exercises i32+i32 → i32 with NARROW CLR types on both
            // sides (uint param, byte param, returns int). Tests the
            // CONV emit path and the param-spill / re-push order.
            [WitSource(@"combine: func(a: u32, b: u8) -> s32;",
                Package = "my:test@1.0.0", Interface = "env",
                Item = "combine")]
            int Combine(uint a, byte b);
        }

        public sealed class TestBundle
        {
            public IEnv Env { get; }
            public TestBundle(IEnv env) { Env = env; }
        }

        private sealed class FakeEnv : IEnv
        {
            private readonly ulong _v;
            public FakeEnv(ulong v) { _v = v; }
            public ulong GetValue() => _v;
            // Distinct compute so the test asserts both sides made
            // it through with the right CONV: a is uint (wide), b
            // is byte (narrow → conv.u1).
            public int Combine(uint a, byte b)
                => unchecked((int)(a * 1000u + b));
        }

        // ============== Wasm fixture =============================
        //
        // (module
        //   (type $t (func (result i64)))
        //   (import "my:test/env@1.0.0" "get-value" (func $imp (type $t)))
        //   (func (export "call_get") (result i64)
        //     call $imp))
        //
        // Sections (byte-by-byte, all sizes single-byte LEB):
        //   1  type:    1×(func ()→i64)
        //   2  import:  "my:test/env@1.0.0"."get-value" : type 0
        //   3  func:    1 local function of type 0
        //   7  export:  "call_get" → func 1
        //  10  code:    body = call 0; end
        private static byte[] BuildDirectLinkedFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 1 type — () → i64
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7E,
            // Import section
            0x02, 0x1F, 0x01,
            // module name: "my:test/env@1.0.0"   (17 bytes)
            0x11,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E,
            0x30,
            // entity name: "get-value"           (9 bytes)
            0x09,
            0x67, 0x65, 0x74, 0x2D, 0x76, 0x61, 0x6C, 0x75, 0x65,
            // desc: func, typeidx 0
            0x00, 0x00,
            // Function section: 1 local function — type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_get" → func 1 (after the import)
            0x07, 0x0C, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x67, 0x65, 0x74,
            0x00, 0x01,
            // Code section: body = call 0; end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (type $t1 (func (result i64)))
        //   (import "my:test/env@1.0.0" "get-value" (func $imp1 (type $t1)))
        //   (import "external" "stub" (func $imp2 (type $t1)))
        //   (func (export "call_resolved") (result i64) call $imp1)
        //   (func (export "call_fallback") (result i64) call $imp2))
        //
        // The "my:test/env@1.0.0"."get-value" import is matched by
        // the resolver and lowers to direct-linked IL. The
        // "external"."stub" import is NOT in the resolver's host
        // package, so it falls back to the legacy
        // ImportDelegates[] dispatch. This exercises the
        // per-funcIdx binding map's "sparse subset" handling.
        private static byte[] BuildMixedFallbackFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 1 type — () → i64
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7E,
            // Import section: 2 imports
            0x02, 0x2F, 0x02,
            // Import 0: "my:test/env@1.0.0" "get-value"
            0x11,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            0x09,
            0x67, 0x65, 0x74, 0x2D, 0x76, 0x61, 0x6C, 0x75, 0x65,
            0x00, 0x00,
            // Import 1: "external" "stub"
            0x08,
            0x65, 0x78, 0x74, 0x65, 0x72, 0x6E, 0x61, 0x6C,
            0x04,
            0x73, 0x74, 0x75, 0x62,
            0x00, 0x00,
            // Function section: 2 local funcs, both type 0
            0x03, 0x03, 0x02, 0x00, 0x00,
            // Export section: 2 exports
            0x07, 0x21, 0x02,
            // "call_resolved" → func 2
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x72, 0x65, 0x73,
            0x6F, 0x6C, 0x76, 0x65, 0x64,
            0x00, 0x02,
            // "call_fallback" → func 3
            0x0D,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x66, 0x61, 0x6C,
            0x6C, 0x62, 0x61, 0x63, 0x6B,
            0x00, 0x03,
            // Code section: 2 bodies, each = call N; end
            // size = count(1) + body0(5) + body1(5) = 11 = 0x0B
            0x0A, 0x0B, 0x02,
            0x04, 0x00, 0x10, 0x00, 0x0B,
            0x04, 0x00, 0x10, 0x01, 0x0B,
        };

        // (module
        //   (type $t (func (param i32 i32) (result i32)))
        //   (import "my:test/env@1.0.0" "combine" (func $imp (type $t)))
        //   (func (export "call_combine") (param i32 i32) (result i32)
        //     local.get 0; local.get 1; call $imp))
        private static byte[] BuildMultiParamFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: (i32 i32) → i32
            0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            // Import section
            0x02, 0x1D, 0x01,
            // module: "my:test/env@1.0.0" (17)
            0x11,
            0x6D, 0x79, 0x3A, 0x74, 0x65, 0x73, 0x74, 0x2F,
            0x65, 0x6E, 0x76, 0x40, 0x31, 0x2E, 0x30, 0x2E, 0x30,
            // entity: "combine" (7)
            0x07,
            0x63, 0x6F, 0x6D, 0x62, 0x69, 0x6E, 0x65,
            // desc: func, typeidx 0
            0x00, 0x00,
            // Function section: 1 local — type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_combine" → func 1
            0x07, 0x10, 0x01,
            0x0C,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x63, 0x6F, 0x6D,
            0x62, 0x69, 0x6E, 0x65,
            0x00, 0x01,
            // Code: local.get 0; local.get 1; call 0; end
            0x0A, 0x0A, 0x01, 0x08, 0x00, 0x20, 0x00, 0x20, 0x01, 0x10, 0x00, 0x0B,
        };

        // ============== Test ====================================

        [Fact]
        public void DirectLinkedImport_BypassesDelegateTable()
        {
            const ulong Sentinel = 0xDEADBEEF12345678UL;

            // Reset the global init registries so this test is
            // hermetic — other tests in the project rely on them too.
            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            // Transpile-time runtime needs the import bound for
            // InstantiateModule to succeed. The stub throws if
            // anyone actually invokes it — proving direct-linked
            // bypass requires this delegate to *not* be called.
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<long>>(
                ("my:test/env@1.0.0", "get-value"),
                () => throw new InvalidOperationException(
                    "stub host fn should not be invoked when "
                    + "direct-linked dispatch is in effect"));

            using var ms = new MemoryStream(
                BuildDirectLinkedFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            // Build the resolver from THIS test assembly (so it
            // sees IEnv + TestBundle) and pass it on the options.
            // Explicit bundleType skips the WasiPreview2Bundle
            // auto-discovery path.
            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm }, bundleType: typeof(TestBundle));

            // Sanity: resolver matched the import.
            Assert.True(resolver.TryResolve("my:test/env@1.0.0",
                "get-value", out var binding));
            Assert.Equal(typeof(IEnv), binding.InterfaceType);
            Assert.Equal(nameof(IEnv.GetValue), binding.Method.Name);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.DirectLinked", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // The resolver-import-bindings map should now reflect
            // the one matched import.
            Assert.NotNull(options.ResolverImportBindings);
            Assert.Single(options.ResolverImportBindings!);

            // Build the IImports proxy. Its handler throws — proves
            // direct linking when the test still passes.
            var importsInterface = result.ImportsInterface!;
            var importsProxy = ImportDispatcher.Create(
                importsInterface,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_env_1_0_0_get_value"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub should not be invoked "
                            + "for direct-linked import"),
                });

            // Construct the generated module class with
            // (importsProxy, bundle). The ctor signature is built
            // by ModuleClassGenerator.EmitConstructor.
            var bundle = new TestBundle(new FakeEnv(Sentinel));
            var moduleType = result.ModuleClass!;
            var instance = Activator.CreateInstance(moduleType,
                new object[] { importsProxy, bundle })!;

            // Find IExports.call_get, invoke it.
            var exportsInterface = result.ExportsInterface!;
            var callGet = exportsInterface.GetMethod(
                InterfaceGenerator.SanitizeName("call_get"))!;
            object? raw = callGet.Invoke(instance, Array.Empty<object>());

            // The export returns wasm i64 → CLR long. The bundle
            // returns ulong (Sentinel); CIL stack form is identical
            // (64-bit int), so casting to ulong recovers the value.
            Assert.IsType<long>(raw);
            Assert.Equal(Sentinel, unchecked((ulong)(long)raw));
        }

        [Fact]
        public void DirectLinkedImport_MultiParam_PassesNarrowConvs()
        {
            // Wasm i32+i32 → i32 import maps to a CLR
            // (uint, byte) → int interface method. Exercises:
            //   - 2-arg spill / re-push order
            //   - wasm i32 arg → CLR uint     (no conv)
            //   - wasm i32 arg → CLR byte     (conv.u1)
            //   - CLR int return → wasm i32   (no conv)

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            runtime.BindHostFunction<Func<int, int, int>>(
                ("my:test/env@1.0.0", "combine"),
                (a, b) => throw new InvalidOperationException(
                    "stub host fn must not be called"));

            using var ms = new MemoryStream(
                BuildMultiParamFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm }, bundleType: typeof(TestBundle));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.DirectLinkedMulti", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_env_1_0_0_combine"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub must not be invoked"),
                });

            var bundle = new TestBundle(new FakeEnv(0));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Compute a*1000 + b for a known (a, b) pair. Picking
            // values whose b > 127 to exercise the conv.u1 path
            // (signed-vs-unsigned narrow). 200u → byte 200.
            uint a = 12345u;
            byte b = 200;
            int expected = unchecked((int)(a * 1000u + b));

            var callCombine = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_combine"))!;
            object? raw = callCombine.Invoke(instance,
                new object?[] { unchecked((int)a), (int)b });

            Assert.IsType<int>(raw);
            Assert.Equal(expected, (int)raw);
        }

        [Fact]
        public void DirectLinkedImport_MixedResolvedAndFallback_BothPathsWork()
        {
            // Two imports in one module. The first is in the
            // resolver's host package and lowers to direct-linked
            // IL; the second is NOT and falls back to the legacy
            // ImportDelegates[] dispatch. The IImports stub for the
            // resolved one throws if called (proves bypass); the
            // stub for the unresolved one returns a known value
            // (proves the legacy path still works alongside the new).
            const ulong DirectLinkedValue = 0xAAAA_BBBB_CCCC_DDDDUL;
            const long FallbackValue = unchecked((long)0x1111_2222_3333_4444UL);

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            // Resolved import: stub throws (must not be invoked).
            runtime.BindHostFunction<Func<long>>(
                ("my:test/env@1.0.0", "get-value"),
                () => throw new InvalidOperationException(
                    "direct-linked stub must not be invoked"));
            // Unresolved import: real handler — the legacy
            // delegate dispatch will route through it.
            runtime.BindHostFunction<Func<long>>(
                ("external", "stub"), () => FallbackValue);

            using var ms = new MemoryStream(
                BuildMixedFallbackFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IEnv).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm }, bundleType: typeof(TestBundle));
            // Sanity: resolver matched ONLY the my:test entry, not
            // the external one.
            Assert.True(resolver.TryResolve(
                "my:test/env@1.0.0", "get-value", out _));
            Assert.False(resolver.TryResolve(
                "external", "stub", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.MixedFallback", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // Per-funcIdx binding map should hold exactly one
            // entry — the resolved import's slot.
            Assert.Single(options.ResolverImportBindings!);

            // IImports proxy: the resolved entry throws if invoked,
            // the fallback entry returns FallbackValue. Both paths
            // get exercised by separate exports.
            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    ["my_test_env_1_0_0_get_value"] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for direct-linked "
                            + "import must not be invoked"),
                    ["external_stub"] = _ => FallbackValue,
                });

            var bundle = new TestBundle(new FakeEnv(DirectLinkedValue));
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

            // Direct-linked path: should hit the bundle.
            var callResolved = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_resolved"))!;
            object? rDirect = callResolved.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<long>(rDirect);
            Assert.Equal(DirectLinkedValue,
                unchecked((ulong)(long)rDirect));

            // Fallback path: should hit the IImports stub.
            var callFallback = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_fallback"))!;
            object? rFallback = callFallback.Invoke(instance,
                Array.Empty<object>());
            Assert.IsType<long>(rFallback);
            Assert.Equal(FallbackValue, (long)rFallback);
        }
    }
}
