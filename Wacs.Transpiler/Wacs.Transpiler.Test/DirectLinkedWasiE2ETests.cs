// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.Preview2.Random;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// End-to-end test using the production WASI Preview 2 host
    /// package as the direct-linked import target. Wasm imports
    /// <c>wasi:random/random.get-random-u64</c>; the typed CLR
    /// callvirt is <see cref="IRandom.GetRandomU64"/>; the IRandom
    /// impl is replaced with a stub that returns a fixed value
    /// so the test can assert the wasm export's result.
    /// </summary>
    public class DirectLinkedWasiE2ETests
    {
        // (module
        //   (type $tRand (func (result i64)))                ;; get-random-u64
        //   (type $tExit (func (param i32)))                 ;; exit-with-code
        //   (type $tClock (func (result i64)))               ;; monotonic-clock.now
        //   (type $tEntry (func (param i32) (result i64)))   ;; call_all(exitCode) → randVal+now
        //   (import "wasi:random/random@0.2.8" "get-random-u64"
        //           (func $rand (type $tRand)))
        //   (import "wasi:cli/exit@0.2.8" "exit-with-code"
        //           (func $exit (type $tExit)))
        //   (import "wasi:clocks/monotonic-clock@0.2.8" "now"
        //           (func $now (type $tClock)))
        //   (func (export "call_all") (param i32) (result i64)
        //     local.get 0
        //     call $exit             ;; ExitWithCode(arg0)
        //     call $rand             ;; → i64
        //     call $now              ;; → i64
        //     i64.add)
        //
        // Three direct-linked imports across three distinct WASI
        // interfaces (IRandom, IExit, IMonotonicClock). The single
        // export proves all three resolve cleanly through the
        // bundle, dispatching to different bundle properties.
        private static byte[] BuildMultiImportFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 4 types
            // 0: () → i64               (4 bytes)
            // 1: (i32) → ()             (4 bytes)
            // 2: () → i64               (4 bytes)  (same as 0 but kept for clarity)
            // 3: (i32) → i64            (5 bytes)
            // size = 1 + 4*3 + 5 = 18 = 0x12
            0x01, 0x12, 0x04,
            0x60, 0x00, 0x01, 0x7E,
            0x60, 0x01, 0x7F, 0x00,
            0x60, 0x00, 0x01, 0x7E,
            0x60, 0x01, 0x7F, 0x01, 0x7E,
            // Import section: 3 imports
            // imp0: wasi:random/random@0.2.8 (24) . get-random-u64 (14) : type 0
            //       = 1+24+1+14+2 = 42
            // imp1: wasi:cli/exit@0.2.8 (19) . exit-with-code (14) : type 1
            //       = 1+19+1+14+2 = 37
            // imp2: wasi:clocks/monotonic-clock@0.2.8 (33) . now (3) : type 2
            //       = 1+33+1+3+2 = 40
            // size = 1 + 42 + 37 + 40 = 120 = 0x78
            0x02, 0x78, 0x03,
            // imp0 — module 24 bytes
            0x18,
            0x77, 0x61, 0x73, 0x69, 0x3A, 0x72, 0x61, 0x6E,
            0x64, 0x6F, 0x6D, 0x2F, 0x72, 0x61, 0x6E, 0x64,
            0x6F, 0x6D, 0x40, 0x30, 0x2E, 0x32, 0x2E, 0x33,
            0x0E,
            0x67, 0x65, 0x74, 0x2D, 0x72, 0x61, 0x6E, 0x64,
            0x6F, 0x6D, 0x2D, 0x75, 0x36, 0x34,
            0x00, 0x00,
            // imp1 — module 19 bytes
            0x13,
            0x77, 0x61, 0x73, 0x69, 0x3A, 0x63, 0x6C, 0x69,
            0x2F, 0x65, 0x78, 0x69, 0x74, 0x40, 0x30, 0x2E,
            0x32, 0x2E, 0x33,
            0x0E,
            0x65, 0x78, 0x69, 0x74, 0x2D, 0x77, 0x69, 0x74,
            0x68, 0x2D, 0x63, 0x6F, 0x64, 0x65,
            0x00, 0x01,
            // imp2 — module 33 bytes
            0x21,
            0x77, 0x61, 0x73, 0x69, 0x3A, 0x63, 0x6C, 0x6F,
            0x63, 0x6B, 0x73, 0x2F, 0x6D, 0x6F, 0x6E, 0x6F,
            0x74, 0x6F, 0x6E, 0x69, 0x63, 0x2D, 0x63, 0x6C,
            0x6F, 0x63, 0x6B, 0x40, 0x30, 0x2E, 0x32, 0x2E, 0x33,
            0x03,
            0x6E, 0x6F, 0x77,
            0x00, 0x02,
            // Function section: 1 local func of type 3
            0x03, 0x02, 0x01, 0x03,
            // Export: "call_all" (8) → func 3 (after 3 imports)
            0x07, 0x0C, 0x01,
            0x08,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x61, 0x6C, 0x6C,
            0x00, 0x03,
            // Code: locals=0, local.get 0, call 1 (exit), call 0 (rand), call 2 (now), i64.add, end
            // body = locals(1) + local.get(2) + call(2)*3 + i64.add(1) + end(1) = 11
            0x0A, 0x0D, 0x01, 0x0B,
            0x00,
            0x20, 0x00,        // local.get 0
            0x10, 0x01,        // call 1 (exit)
            0x10, 0x00,        // call 0 (rand)
            0x10, 0x02,        // call 2 (now)
            0x7C,              // i64.add
            0x0B,
        };

        // (module
        //   (type $t (func (result i64)))
        //   (import "wasi:random/random@0.2.8" "get-random-u64"
        //           (func $imp (type $t)))
        //   (func (export "call_random") (result i64) call $imp))
        private static byte[] BuildRandomFixtureWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: () → i64
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7E,
            // Import section
            // size = count(1) + modlen(1) + mod(24) + entlen(1) + ent(14) + desc(2) = 43 = 0x2B
            0x02, 0x2B, 0x01,
            // module: "wasi:random/random@0.2.8" (24 bytes)
            0x18,
            0x77, 0x61, 0x73, 0x69, 0x3A, 0x72, 0x61, 0x6E,
            0x64, 0x6F, 0x6D, 0x2F, 0x72, 0x61, 0x6E, 0x64,
            0x6F, 0x6D, 0x40, 0x30, 0x2E, 0x32, 0x2E, 0x33,
            // entity: "get-random-u64" (14)
            0x0E,
            0x67, 0x65, 0x74, 0x2D, 0x72, 0x61, 0x6E, 0x64,
            0x6F, 0x6D, 0x2D, 0x75, 0x36, 0x34,
            // desc: func, type 0
            0x00, 0x00,
            // Function section: 1 func of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export: "call_random" (11) → func 1
            0x07, 0x0F, 0x01,
            0x0B,
            0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x72, 0x61, 0x6E,
            0x64, 0x6F, 0x6D,
            0x00, 0x01,
            // Code: locals=0, call 0, end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x10, 0x00, 0x0B,
        };

        // Test impl of IRandom — returns a fixed sentinel so the
        // assertion is deterministic. Substitutes for the
        // production Wacs.WASI.Preview2.Random.Random impl.
        private sealed class FixedRandom : IRandom
        {
            private readonly ulong _v;
            public FixedRandom(ulong v) { _v = v; }
            public ulong GetRandomU64() => _v;
            public byte[] GetRandomBytes(ulong len)
                => new byte[(int)len];
        }

        // Test impls for the multi-interface E2E test.
        private sealed class FixedClock : Wacs.WASI.Preview2.Clocks.IMonotonicClock
        {
            private readonly ulong _now;
            public FixedClock(ulong now) { _now = now; }
            public ulong Now() => _now;
            public ulong Resolution() => 1;
            public Wacs.WASI.Preview2.Io.IPollable SubscribeInstant(ulong w) => throw new NotImplementedException();
            public Wacs.WASI.Preview2.Io.IPollable SubscribeDuration(ulong w) => throw new NotImplementedException();
        }

        // Captures the exit code for assertion (real impl throws
        // ExitException; we record the code instead).
        private sealed class CapturingExit : Wacs.WASI.Preview2.Cli.IExit
        {
            public byte? Code { get; private set; }
            public void Exit(Wacs.ComponentModel.Runtime.Result<Wacs.ComponentModel.Runtime.Unit, Wacs.ComponentModel.Runtime.Unit> _) => throw new NotImplementedException();
            public void ExitWithCode(byte statusCode) { Code = statusCode; }
        }

        [Fact]
        public void E2E_WasiRandomU64_DirectLinkedThroughBundle()
        {
            const ulong Sentinel = 0x0FFEE_BABE_DEAD_F00DUL;

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            // The production binding registers via AddWasiPreview2;
            // here we just need a stub host fn so InstantiateModule
            // is satisfied. The direct-linked IL bypasses this.
            runtime.BindHostFunction<Func<long>>(
                ("wasi:random/random@0.2.8", "get-random-u64"),
                () => throw new InvalidOperationException(
                    "stub host fn must not be invoked when "
                    + "direct linking is in effect"));

            using var ms = new MemoryStream(BuildRandomFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            // Resolver loads the production WASI host package.
            // PreferredBundleType auto-resolves to WasiPreview2Bundle.
            var hostAsm = typeof(IRandom).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm });
            Assert.Equal(typeof(WasiPreview2Bundle),
                resolver.PreferredBundleType);

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.WasiRandomE2E", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // Sanity: the resolver matched the import.
            Assert.Single(options.ResolverImportBindings!);

            // Stub IImports — should never fire (direct-linked).
            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "wasi:random/random@0.2.8_get-random-u64")] = _ =>
                        throw new InvalidOperationException(
                            "IImports stub for get-random-u64 must "
                            + "not be invoked"),
                });

            // Construct the production WasiPreview2Bundle, but with
            // our stub IRandom in place of the default. The bundle
            // ctor takes 22 typed interfaces; we supply non-null
            // proxies for the others (they're never touched).
            var bundle = new WasiPreview2Bundle(
                environment: new StubEnv(),
                exit: new StubExit(),
                stdin: new StubStdin(),
                stdout: new StubStdout(),
                stderr: new StubStderr(),
                terminalStdin: new StubTermStdin(),
                terminalStdout: new StubTermStdout(),
                terminalStderr: new StubTermStderr(),
                monotonicClock: new StubMonotonic(),
                wallClock: new StubWall(),
                timezone: new StubTimezone(),
                random: new FixedRandom(Sentinel),
                insecure: new StubInsecure(),
                insecureSeed: new StubInsecureSeed(),
                poll: new StubPoll(),
                preopens: new StubPreopens(),
                filesystemErrorCode: new StubFsErr(),
                instanceNetwork: new StubInstNet(),
                tcpCreateSocket: new StubTcp(),
                udpCreateSocket: new StubUdp(),
                ipNameLookup: new StubDns(),
                outgoingHandler: new StubHttpHandler());

            // Module ctor: (importsProxy, bundle, resources?). The
            // production WasiPreview2 resolver auto-discovers a
            // resources class when the bundle is the production
            // WasiPreview2Bundle and resource interfaces appear on
            // it (which they do — IOutputStream etc). Free-fn
            // tests like this don't exercise the resources, but
            // the ctor signature still demands the 3rd slot.
            var resources = new Wacs.WASI.Preview2.DependencyInjection
                .WasiPreview2Resources(
                    new Wacs.WASI.Preview2.HostBinding.ResourceContext());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callRandom = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_random"))!;
            object? raw = callRandom.Invoke(instance,
                Array.Empty<object>());

            // The wasm i64 result IS the ulong sentinel returned by
            // FixedRandom.GetRandomU64() — proves the direct-linked
            // IL went through the production bundle, found IRandom,
            // dispatched our typed callvirt, and returned cleanly.
            Assert.IsType<long>(raw);
            Assert.Equal(Sentinel, unchecked((ulong)(long)raw));
        }

        [Fact]
        public void E2E_MultiInterfaceImports_AllResolveThroughBundle()
        {
            // Three direct-linked imports across three distinct
            // WASI interfaces in one module. Tests:
            //   - per-funcIdx binding map handling 3 entries
            //   - bundle property dispatch to 3 different I*
            //     types in one transpile
            //   - varied wire shapes (no-arg/i64-return,
            //     i32-arg/no-return, no-arg/i64-return)
            //   - method-with-an-arg + multi-import composition

            const ulong RandVal = 0x1111_2222_3333_4444UL;
            const ulong NowVal  = 0x5555_6666_7777_8888UL;
            const byte ExitArg  = 42;

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            // Stubs throw if invoked — direct-linked path bypasses.
            runtime.BindHostFunction<Func<long>>(
                ("wasi:random/random@0.2.8", "get-random-u64"),
                () => throw new InvalidOperationException("rand stub"));
            runtime.BindHostFunction<Action<int>>(
                ("wasi:cli/exit@0.2.8", "exit-with-code"),
                _ => throw new InvalidOperationException("exit stub"));
            runtime.BindHostFunction<Func<long>>(
                ("wasi:clocks/monotonic-clock@0.2.8", "now"),
                () => throw new InvalidOperationException("clock stub"));

            using var ms = new MemoryStream(BuildMultiImportFixtureWasm());
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(IRandom).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm });

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.MultiImportE2E", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // All 3 imports resolved.
            Assert.Equal(3, options.ResolverImportBindings!.Count);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "wasi:random/random@0.2.8_get-random-u64")] = _ =>
                        throw new InvalidOperationException("rand IImports stub"),
                    [InterfaceGenerator.SanitizeName(
                        "wasi:cli/exit@0.2.8_exit-with-code")] = _ =>
                        throw new InvalidOperationException("exit IImports stub"),
                    [InterfaceGenerator.SanitizeName(
                        "wasi:clocks/monotonic-clock@0.2.8_now")] = _ =>
                        throw new InvalidOperationException("clock IImports stub"),
                });

            var capturingExit = new CapturingExit();
            var bundle = new WasiPreview2Bundle(
                environment: new StubEnv(),
                exit: capturingExit,
                stdin: new StubStdin(), stdout: new StubStdout(),
                stderr: new StubStderr(),
                terminalStdin: new StubTermStdin(),
                terminalStdout: new StubTermStdout(),
                terminalStderr: new StubTermStderr(),
                monotonicClock: new FixedClock(NowVal),
                wallClock: new StubWall(),
                timezone: new StubTimezone(),
                random: new FixedRandom(RandVal),
                insecure: new StubInsecure(),
                insecureSeed: new StubInsecureSeed(),
                poll: new StubPoll(),
                preopens: new StubPreopens(),
                filesystemErrorCode: new StubFsErr(),
                instanceNetwork: new StubInstNet(),
                tcpCreateSocket: new StubTcp(),
                udpCreateSocket: new StubUdp(),
                ipNameLookup: new StubDns(),
                outgoingHandler: new StubHttpHandler());

            // Module ctor: (importsProxy, bundle, resources?). The
            // production WasiPreview2 resolver auto-discovers a
            // resources class when the bundle is the production
            // WasiPreview2Bundle and resource interfaces appear on
            // it (which they do — IOutputStream etc). Free-fn
            // tests like this don't exercise the resources, but
            // the ctor signature still demands the 3rd slot.
            var resources = new Wacs.WASI.Preview2.DependencyInjection
                .WasiPreview2Resources(
                    new Wacs.WASI.Preview2.HostBinding.ResourceContext());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var callAll = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("call_all"))!;
            object? raw = callAll.Invoke(instance,
                new object?[] { (int)ExitArg });

            // ExitWithCode received the arg.
            Assert.Equal(ExitArg, capturingExit.Code);

            // i64.add(RandVal, NowVal) — wraps via unchecked.
            Assert.IsType<long>(raw);
            Assert.Equal(unchecked(RandVal + NowVal),
                unchecked((ulong)(long)raw));
        }

        [Fact]
        public void E2E_Preopens_GetDirectories_ListResourceStringTuple()
        {
            // wasi:filesystem/preopens.get-directories returns
            // list&lt;tuple&lt;own&lt;descriptor&gt;, string&gt;&gt;. The fixture's
            // exported `count` calls get-directories, drops every
            // descriptor handle (i32.load at outerPtr + i*12), and
            // returns the count.
            //
            // This test wires the production WasiPreview2Bundle with
            // a stub preopens that hands back three (descriptor,
            // path) pairs and verifies the transpiled, direct-linked
            // emit lifts the list correctly: per-element
            // AllocateResource for the own&lt;descriptor&gt; field plus
            // cabi_realloc + UTF-8 encode for the string field, all
            // packed into the 12-byte stride the wasm probes. Closes
            // gap 9 from wasi-nn/WACS-GAPS.md (preopens not reaching
            // the guest under the transpiler engine).

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            // Trap-stub the imports so runtime.InstantiateModule's
            // import resolution succeeds. Direct-link IL bypasses
            // these — they fire only if the path falls back.
            runtime.BindHostFunction<Action<int>>(
                ("wasi:filesystem/preopens@0.2.8", "get-directories"),
                _ => throw new InvalidOperationException(
                    "stub get-directories must not be invoked when "
                    + "direct linking is in effect"));
            runtime.BindHostFunction<Action<int>>(
                ("wasi:filesystem/types@0.2.8",
                    "[resource-drop]descriptor"),
                _ => { /* drops are runtime-side; benign */ });

            var fixturePath = FindFixturePath(
                "wasi-preopens-component", "po.component.wasm");
            // The fixture is a component; pull the embedded core
            // module via ComponentTranspiler.ParseFile so we can
            // transpile the core directly (the test exercises the
            // core module path, not the full component-instantiate
            // flow).
            var parsed = ComponentTranspiler.ParseFile(fixturePath);
            var module = parsed.CoreModules[0];
            var moduleInst = runtime.InstantiateModule(module);

            var hostAsm = typeof(Wacs.WASI.Preview2.Filesystem
                .IPreopens).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm });
            Assert.Equal(typeof(WasiPreview2Bundle),
                resolver.PreferredBundleType);
            // get-directories MUST resolve through the bundle path
            // — the resolver-aware aggregate predicate is what
            // drives this test.
            Assert.True(resolver.TryResolve(
                "wasi:filesystem/preopens@0.2.8",
                "get-directories", out _));

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.PreopensE2E", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "WasmModule");

            // get-directories at minimum lands in the binding map
            // — that's the import this test cares about. Resource-
            // drop intrinsics may or may not be resolver-tracked
            // depending on host-package convention; we don't assert
            // on it.
            Assert.True(options.ResolverImportBindings!.Count >= 1);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "wasi:filesystem/preopens@0.2.8_get-directories")]
                        = _ => throw new InvalidOperationException(
                            "IImports stub for get-directories must "
                            + "not be invoked"),
                });

            var preopens = new FixedPreopens(new (
                Wacs.WASI.Preview2.Filesystem.IDescriptor, string)[]
                {
                    (new Wacs.WASI.Preview2.Filesystem.Descriptor("/"),
                        "/"),
                    (new Wacs.WASI.Preview2.Filesystem.Descriptor(
                        "/tmp"), "/tmp"),
                    (new Wacs.WASI.Preview2.Filesystem.Descriptor(
                        "/home"), "/home"),
                });
            var bundle = new WasiPreview2Bundle(
                environment: new StubEnv(),
                exit: new StubExit(),
                stdin: new StubStdin(),
                stdout: new StubStdout(),
                stderr: new StubStderr(),
                terminalStdin: new StubTermStdin(),
                terminalStdout: new StubTermStdout(),
                terminalStderr: new StubTermStderr(),
                monotonicClock: new StubMonotonic(),
                wallClock: new StubWall(),
                timezone: new StubTimezone(),
                random: new StubRandom(),
                insecure: new StubInsecure(),
                insecureSeed: new StubInsecureSeed(),
                poll: new StubPoll(),
                preopens: preopens,
                filesystemErrorCode: new StubFsErr(),
                instanceNetwork: new StubInstNet(),
                tcpCreateSocket: new StubTcp(),
                udpCreateSocket: new StubUdp(),
                ipNameLookup: new StubDns(),
                outgoingHandler: new StubHttpHandler());

            var resources = new WasiPreview2Resources(
                new Wacs.WASI.Preview2.HostBinding.ResourceContext());
            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var countMethod = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("count"))!;
            object? raw = countMethod.Invoke(instance,
                Array.Empty<object>());

            // Three preopens lifted, count returned cleanly. The
            // stub IImports throws if reached, so a non-zero return
            // proves the direct-link emit assembled the list.
            Assert.IsType<int>(raw);
            Assert.Equal(3, (int)raw!);
        }

        private static string FindFixturePath(string fixtureDir,
            string fileName)
        {
            var dir = new DirectoryInfo(
                Directory.GetCurrentDirectory());
            while (dir != null
                && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test",
                "components", "fixtures", fixtureDir, "wasm",
                fileName);
        }

        private sealed class FixedPreopens
            : Wacs.WASI.Preview2.Filesystem.IPreopens
        {
            private readonly (Wacs.WASI.Preview2.Filesystem.IDescriptor,
                string)[] _entries;
            public FixedPreopens((
                Wacs.WASI.Preview2.Filesystem.IDescriptor, string)[] e)
            { _entries = e; }
            public (Wacs.WASI.Preview2.Filesystem.IDescriptor, string)[]
                GetDirectories() => _entries;
        }

        private sealed class StubRandom
            : Wacs.WASI.Preview2.Random.IRandom
        {
            public ulong GetRandomU64() =>
                throw new NotImplementedException();
            public byte[] GetRandomBytes(ulong len) =>
                throw new NotImplementedException();
        }

        // ---- Stub impls for the rest of the bundle interfaces ----
        // None of these are touched by the wasm-random fixture;
        // they exist only to satisfy WasiPreview2Bundle's ctor.
        // Methods throw if called so a stray dispatch surfaces.

        private sealed class StubEnv : Wacs.WASI.Preview2.Cli.IEnvironment
        {
            public (string, string)[] GetEnvironment() => throw new NotImplementedException();
            public string[] GetArguments() => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Option<string> InitialCwd() => throw new NotImplementedException();
        }
        private sealed class StubExit : Wacs.WASI.Preview2.Cli.IExit
        {
            public void Exit(Wacs.ComponentModel.Runtime.Result<Wacs.ComponentModel.Runtime.Unit, Wacs.ComponentModel.Runtime.Unit> status) => throw new NotImplementedException();
            public void ExitWithCode(byte statusCode) => throw new NotImplementedException();
        }
        private sealed class StubStdin : Wacs.WASI.Preview2.Cli.IStdin
        {
            public Wacs.WASI.Preview2.Io.IInputStream GetStdin() => throw new NotImplementedException();
        }
        private sealed class StubStdout : Wacs.WASI.Preview2.Cli.IStdout
        {
            public Wacs.WASI.Preview2.Io.IOutputStream GetStdout() => throw new NotImplementedException();
        }
        private sealed class StubStderr : Wacs.WASI.Preview2.Cli.IStderr
        {
            public Wacs.WASI.Preview2.Io.IOutputStream GetStderr() => throw new NotImplementedException();
        }
        private sealed class StubTermStdin : Wacs.WASI.Preview2.Cli.ITerminalStdin
        {
            public Wacs.ComponentModel.Runtime.Option<Wacs.WASI.Preview2.Cli.ITerminalInput> GetTerminalStdin() => throw new NotImplementedException();
        }
        private sealed class StubTermStdout : Wacs.WASI.Preview2.Cli.ITerminalStdout
        {
            public Wacs.ComponentModel.Runtime.Option<Wacs.WASI.Preview2.Cli.ITerminalOutput> GetTerminalStdout() => throw new NotImplementedException();
        }
        private sealed class StubTermStderr : Wacs.WASI.Preview2.Cli.ITerminalStderr
        {
            public Wacs.ComponentModel.Runtime.Option<Wacs.WASI.Preview2.Cli.ITerminalOutput> GetTerminalStderr() => throw new NotImplementedException();
        }
        private sealed class StubMonotonic : Wacs.WASI.Preview2.Clocks.IMonotonicClock
        {
            public ulong Now() => throw new NotImplementedException();
            public ulong Resolution() => throw new NotImplementedException();
            public Wacs.WASI.Preview2.Io.IPollable SubscribeInstant(ulong when) => throw new NotImplementedException();
            public Wacs.WASI.Preview2.Io.IPollable SubscribeDuration(ulong when) => throw new NotImplementedException();
        }
        private sealed class StubWall : Wacs.WASI.Preview2.Clocks.IWallClock
        {
            public Wacs.WASI.Preview2.Clocks.Datetime Now() => throw new NotImplementedException();
            public Wacs.WASI.Preview2.Clocks.Datetime Resolution() => throw new NotImplementedException();
        }
        private sealed class StubTimezone : Wacs.WASI.Preview2.Clocks.ITimezone
        {
            public int UtcOffset(Wacs.WASI.Preview2.Clocks.Datetime _) => throw new NotImplementedException();
            public Wacs.WASI.Preview2.Clocks.TimezoneDisplay Display(Wacs.WASI.Preview2.Clocks.Datetime _) => throw new NotImplementedException();
        }
        private sealed class StubInsecure : Wacs.WASI.Preview2.Random.IInsecure
        {
            public ulong GetInsecureRandomU64() => throw new NotImplementedException();
            public byte[] GetInsecureRandomBytes(ulong len) => throw new NotImplementedException();
        }
        private sealed class StubInsecureSeed : Wacs.WASI.Preview2.Random.IInsecureSeed
        {
            public (ulong, ulong) InsecureSeed() => throw new NotImplementedException();
        }
        private sealed class StubPoll : Wacs.WASI.Preview2.Io.IPoll
        {
            public uint[] Poll(Wacs.WASI.Preview2.Io.IPollable[] @in) => throw new NotImplementedException();
        }
        private sealed class StubPreopens : Wacs.WASI.Preview2.Filesystem.IPreopens
        {
            public (Wacs.WASI.Preview2.Filesystem.IDescriptor, string)[] GetDirectories() => throw new NotImplementedException();
        }
        private sealed class StubFsErr : Wacs.WASI.Preview2.Filesystem.IFilesystemErrorCode
        {
            public Wacs.ComponentModel.Runtime.Option<Wacs.WASI.Preview2.Filesystem.ErrorCode> FilesystemErrorCode(Wacs.WASI.Preview2.Io.Error _) => throw new NotImplementedException();
        }
        private sealed class StubInstNet : Wacs.WASI.Preview2.Sockets.IInstanceNetwork
        {
            public Wacs.WASI.Preview2.Sockets.INetwork InstanceNetwork() => throw new NotImplementedException();
        }
        private sealed class StubTcp : Wacs.WASI.Preview2.Sockets.ITcpCreateSocket
        {
            public Wacs.ComponentModel.Runtime.Result<Wacs.WASI.Preview2.Sockets.ITcpSocket, Wacs.WASI.Preview2.Sockets.ErrorCode> CreateTcpSocket(Wacs.WASI.Preview2.Sockets.IpAddressFamily _) => throw new NotImplementedException();
        }
        private sealed class StubUdp : Wacs.WASI.Preview2.Sockets.IUdpCreateSocket
        {
            public Wacs.ComponentModel.Runtime.Result<Wacs.WASI.Preview2.Sockets.IUdpSocket, Wacs.WASI.Preview2.Sockets.ErrorCode> CreateUdpSocket(Wacs.WASI.Preview2.Sockets.IpAddressFamily _) => throw new NotImplementedException();
        }
        private sealed class StubDns : Wacs.WASI.Preview2.Sockets.IIpNameLookup
        {
            public Wacs.ComponentModel.Runtime.Result<Wacs.WASI.Preview2.Sockets.IResolveAddressStream, Wacs.WASI.Preview2.Sockets.ErrorCode> ResolveAddresses(Wacs.WASI.Preview2.Sockets.INetwork _, string __) => throw new NotImplementedException();
        }
        private sealed class StubHttpHandler : Wacs.WASI.Preview2.Http.IOutgoingHandler
        {
            public Wacs.ComponentModel.Runtime.Result<Wacs.WASI.Preview2.Http.IFutureIncomingResponse, Wacs.WASI.Preview2.Http.ErrorCode> Handle(Wacs.WASI.Preview2.Http.IOutgoingRequest req, Wacs.ComponentModel.Runtime.Option<Wacs.WASI.Preview2.Http.IRequestOptions> opts) => throw new NotImplementedException();
        }
    }
}
