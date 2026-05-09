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
            // packed into the 12-byte stride the wasm probes.

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

        [Fact]
        public void E2E_DescriptorStat_RecordWithOptionFields()
        {
            // wasi:filesystem/types.[method]descriptor.stat returns
            // result<descriptor-stat, error-code>. descriptor-stat is
            // a record { type: descriptor-type, link-count: u64,
            // size: u64, three option<datetime> timestamps }. Wires
            // a stub IDescriptor whose Stat() returns a known Size,
            // transpiles the fsstat fixture, reads retArea+24 (the
            // size field within the OK arm). A returned 0 would mean
            // direct-link emit fell back to the IImports default-zero
            // stub; the expected value proves record-with-option-
            // fields emit lifted the result correctly.
            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var runtime = new WasmRuntime();
            // Trap-stub the import so InstantiateModule's import
            // resolution succeeds. Direct-link IL bypasses this —
            // it fires only if the path falls back to IImports.
            runtime.BindHostFunction<Action<int, int>>(
                ("wasi:filesystem/types@0.2.8",
                    "[method]descriptor.stat"),
                (_, __) => throw new InvalidOperationException(
                    "stub descriptor.stat must not be invoked when "
                    + "direct linking is in effect"));

            var fixturePath = FindFixturePath(
                "wasi-fs-stat-component", "fsstat.component.wasm");
            // Some fixtures only ship the wat alongside; fall back
            // to assembling the wat module if no precompiled wasm
            // is on disk.
            // Hand-rolled fixture wasm so the test isn't gated on
            // wat assembly tooling. Imports descriptor.stat with
            // signature (i32, i32) → () and exports ask-stat-size +
            // ask-stat-mtime-disc, both reading from retArea.
            byte[] fixtureBytes = BuildFsStatFixtureWasm();
            using var ms = new MemoryStream(fixtureBytes);
            var coreModule = BinaryModuleParser
                .ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(coreModule);

            var hostAsm = typeof(Wacs.WASI.Preview2.Filesystem
                .IDescriptor).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm });
            // descriptor.stat MUST resolve through the bundle path.
            Assert.True(resolver.TryResolve(
                "wasi:filesystem/types@0.2.8",
                "[method]descriptor.stat", out _));

            // Independent predicate check — the resolver-aware
            // recognition of Result<DescriptorStat, ErrorCode>.
            // Pre-fix this returned false, falling through to the
            // IImports stub.
            var isAggSupportedMethod = typeof(DirectLinkedImportEmit)
                .GetMethod("IsAggregateReturnSupported",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (isAggSupportedMethod != null)
            {
                var resultType = typeof(Wacs.ComponentModel.Runtime
                    .Result<Wacs.WASI.Preview2.Filesystem.DescriptorStat,
                        Wacs.WASI.Preview2.Filesystem.ErrorCode>);
                Assert.True((bool)isAggSupportedMethod.Invoke(null,
                    new object?[] { resultType, resolver })!);
            }

            var options = new TranspilerOptions
            {
                Resolver = resolver,
                HostPackages = new[] { hostAsm },
            };
            var transpiler = new ModuleTranspiler(
                "Wacs.Test.DescriptorStatE2E", options);
            var result = transpiler.Transpile(moduleInst, runtime,
                "FsStatModule");
            Assert.True(options.ResolverImportBindings!.Count >= 1);

            var importsProxy = ImportDispatcher.Create(
                result.ImportsInterface!,
                new Dictionary<string, Func<object?[], object?>>
                {
                    [InterfaceGenerator.SanitizeName(
                        "wasi:filesystem/types@0.2.8_[method]descriptor.stat")]
                        = _ => throw new InvalidOperationException(
                            "IImports stub for descriptor.stat must "
                            + "not be invoked"),
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
                preopens: new StubPreopens(),
                filesystemErrorCode: new StubFsErr(),
                instanceNetwork: new StubInstNet(),
                tcpCreateSocket: new StubTcp(),
                udpCreateSocket: new StubUdp(),
                ipNameLookup: new StubDns(),
                outgoingHandler: new StubHttpHandler());

            var resources = new WasiPreview2Resources(
                new Wacs.WASI.Preview2.HostBinding.ResourceContext());

            // Allocate the stub descriptor BEFORE module construction
            // so the handle is stable. Stat() returns OK with a known
            // Size + None timestamps so retArea+24 (size) and
            // retArea+56 (mtime option disc) both have predictable
            // values for the assertions.
            const ulong ExpectedSize = 0x0000_DEAD_C0DE_F00DUL;
            var stubDescriptor = new StatStubDescriptor(ExpectedSize);
            int handle = resources.AllocateResource(
                typeof(Wacs.WASI.Preview2.Filesystem.IDescriptor),
                stubDescriptor);

            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle, resources })!;

            var sizeMethod = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("ask-stat-size"))!;
            object? rawSize = sizeMethod.Invoke(instance,
                new object[] { handle });

            // ulong (low 64 bits of the size field) returned by the
            // wasm export through i64.load offset=24. Pre-fix this
            // would be 0 (silent IImports fallback); post-fix it
            // matches what the stub Stat() set.
            Assert.IsType<long>(rawSize);
            Assert.Equal(ExpectedSize, unchecked((ulong)(long)rawSize!));

            var discMethod = result.ExportsInterface!.GetMethod(
                InterfaceGenerator.SanitizeName("ask-stat-mtime-disc"));
            if (discMethod != null)
            {
                // mtime was None → option disc at retArea+56 must be 0.
                object? rawDisc = discMethod.Invoke(instance,
                    new object[] { handle });
                Assert.Equal(0, (int)rawDisc!);
            }
        }

        // Minimal fallback wasm equivalent to fsstat.wat — assembled
        // here so the test runs even if the .component.wasm has not
        // been built into the fixture tree. Imports
        // [method]descriptor.stat (i32, i32) → (); exports
        // ask-stat-size (i32) → i64 reading i64 at retArea+24.
        private static byte[] BuildFsStatFixtureWasm()
        {
            // wat:
            //   (module
            //     (import "wasi:filesystem/types@0.2.8"
            //       "[method]descriptor.stat" (func $stat (param i32 i32)))
            //     (memory (export "memory") 1)
            //     (func (export "ask-stat-size") (param i32) (result i64)
            //       (local i32)
            //       i32.const 8
            //       local.set 1
            //       local.get 0
            //       local.get 1
            //       call $stat
            //       local.get 1
            //       i64.load offset=24)
            //     (func (export "ask-stat-mtime-disc") (param i32) (result i32)
            //       (local i32)
            //       i32.const 8
            //       local.set 1
            //       local.get 0
            //       local.get 1
            //       call $stat
            //       local.get 1
            //       i32.load8_u offset=56))
            //
            // Hand-rolled binary: we only need the byte sequence, and
            // assembling via Wat would pull in extra deps.
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            // magic + version
            w.Write((byte)0x00); w.Write((byte)0x61); w.Write((byte)0x73); w.Write((byte)0x6D);
            w.Write((byte)0x01); w.Write((byte)0x00); w.Write((byte)0x00); w.Write((byte)0x00);
            // type section: 3 types
            //   0: (i32, i32) → ()    [stat]
            //   1: (i32) → i64        [ask-stat-size]
            //   2: (i32) → i32        [ask-stat-mtime-disc]
            byte[] typeSec = new byte[]
            {
                0x03,
                0x60, 0x02, 0x7F, 0x7F, 0x00,
                0x60, 0x01, 0x7F, 0x01, 0x7E,
                0x60, 0x01, 0x7F, 0x01, 0x7F,
            };
            w.Write((byte)0x01); WriteLeb(w, (uint)typeSec.Length); w.Write(typeSec);
            // import section: 1 import
            //   "wasi:filesystem/types@0.2.8" (28) . "[method]descriptor.stat" (23) : type 0
            string imodule = "wasi:filesystem/types@0.2.8";
            string iname = "[method]descriptor.stat";
            var imodB = System.Text.Encoding.UTF8.GetBytes(imodule);
            var inmB = System.Text.Encoding.UTF8.GetBytes(iname);
            var iSec = new MemoryStream();
            var iw = new BinaryWriter(iSec);
            iw.Write((byte)0x01); // 1 import
            WriteLeb(iw, (uint)imodB.Length); iw.Write(imodB);
            WriteLeb(iw, (uint)inmB.Length); iw.Write(inmB);
            iw.Write((byte)0x00); iw.Write((byte)0x00); // func, type 0
            w.Write((byte)0x02);
            WriteLeb(w, (uint)iSec.Length); w.Write(iSec.ToArray());
            // function section: 2 local funcs (types 1 and 2)
            w.Write((byte)0x03);
            WriteLeb(w, 3u);
            w.Write((byte)0x02); w.Write((byte)0x01); w.Write((byte)0x02);
            // memory section: 1 memory, min=1
            w.Write((byte)0x05);
            WriteLeb(w, 3u);
            w.Write((byte)0x01); w.Write((byte)0x00); w.Write((byte)0x01);
            // export section: memory + 2 funcs
            //   "memory" (6) → memory 0
            //   "ask-stat-size" (13) → func 1 (after 1 import)
            //   "ask-stat-mtime-disc" (19) → func 2
            var eSec = new MemoryStream();
            var ew = new BinaryWriter(eSec);
            ew.Write((byte)0x03);
            string e0 = "memory";
            var e0B = System.Text.Encoding.UTF8.GetBytes(e0);
            WriteLeb(ew, (uint)e0B.Length); ew.Write(e0B);
            ew.Write((byte)0x02); WriteLeb(ew, 0u);
            string e1 = "ask-stat-size";
            var e1B = System.Text.Encoding.UTF8.GetBytes(e1);
            WriteLeb(ew, (uint)e1B.Length); ew.Write(e1B);
            ew.Write((byte)0x00); WriteLeb(ew, 1u);
            string e2 = "ask-stat-mtime-disc";
            var e2B = System.Text.Encoding.UTF8.GetBytes(e2);
            WriteLeb(ew, (uint)e2B.Length); ew.Write(e2B);
            ew.Write((byte)0x00); WriteLeb(ew, 2u);
            w.Write((byte)0x07);
            WriteLeb(w, (uint)eSec.Length); w.Write(eSec.ToArray());
            // code section: 2 bodies
            //   ask-stat-size body:
            //     locals: 1 i32
            //     i32.const 8 ; local.set 1
            //     local.get 0 ; local.get 1 ; call 0
            //     local.get 1 ; i64.load offset=24, align=3
            //     end
            byte[] body1 = new byte[]
            {
                0x01, 0x01, 0x7F,                        // 1 local of i32
                0x41, 0x08, 0x21, 0x01,                  // i32.const 8 ; local.set 1
                0x20, 0x00, 0x20, 0x01, 0x10, 0x00,      // local.get 0 ; local.get 1 ; call 0
                0x20, 0x01, 0x29, 0x03, 0x18,            // local.get 1 ; i64.load align=3 offset=24
                0x0B,
            };
            byte[] body2 = new byte[]
            {
                0x01, 0x01, 0x7F,                        // 1 local of i32
                0x41, 0x08, 0x21, 0x01,                  // i32.const 8 ; local.set 1
                0x20, 0x00, 0x20, 0x01, 0x10, 0x00,      // local.get 0 ; local.get 1 ; call 0
                0x20, 0x01, 0x2D, 0x00, 0x38,            // local.get 1 ; i32.load8_u align=0 offset=56
                0x0B,
            };
            var cSec = new MemoryStream();
            var cw = new BinaryWriter(cSec);
            cw.Write((byte)0x02);
            WriteLeb(cw, (uint)body1.Length); cw.Write(body1);
            WriteLeb(cw, (uint)body2.Length); cw.Write(body2);
            w.Write((byte)0x0A);
            WriteLeb(w, (uint)cSec.Length); cw.Flush();
            w.Write(cSec.ToArray());

            return stream.ToArray();
        }

        private static void WriteLeb(BinaryWriter w, uint value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0) { w.Write(b); return; }
                w.Write((byte)(b | 0x80));
            }
        }

        // Stub IDescriptor whose Stat() returns a hand-shaped
        // DescriptorStat with a known Size and all-None timestamps.
        // Every other method throws — this fixture only exercises
        // Stat().
        private sealed class StatStubDescriptor
            : Wacs.WASI.Preview2.Filesystem.IDescriptor
        {
            private readonly ulong _size;
            public StatStubDescriptor(ulong size) { _size = size; }

            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.DescriptorStat,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> Stat()
                => Wacs.ComponentModel.Runtime.Result<
                    Wacs.WASI.Preview2.Filesystem.DescriptorStat,
                    Wacs.WASI.Preview2.Filesystem.ErrorCode>.FromOk(
                    new Wacs.WASI.Preview2.Filesystem.DescriptorStat
                    {
                        Type = Wacs.WASI.Preview2.Filesystem
                            .DescriptorType.RegularFile,
                        LinkCount = 1,
                        Size = _size,
                        DataAccessTimestamp = Wacs.ComponentModel
                            .Runtime.Option<Wacs.WASI.Preview2.Clocks
                                .Datetime>.None,
                        DataModificationTimestamp = Wacs.ComponentModel
                            .Runtime.Option<Wacs.WASI.Preview2.Clocks
                                .Datetime>.None,
                        StatusChangeTimestamp = Wacs.ComponentModel
                            .Runtime.Option<Wacs.WASI.Preview2.Clocks
                                .Datetime>.None,
                    });

            // Every other method on IDescriptor — irrelevant to the
            // gap-10 path, throw so a stray dispatch is loud.
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Io.IInputStream,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                ReadViaStream(ulong offset)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Io.IOutputStream,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                WriteViaStream(ulong offset)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Io.IOutputStream,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                AppendViaStream()
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                Advise(ulong offset, ulong length,
                    Wacs.WASI.Preview2.Filesystem.Advice advice)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> SyncData()
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.DescriptorFlags,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> GetFlags()
                => throw new NotImplementedException();
            public new Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.DescriptorType,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> GetType()
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> SetSize(ulong size)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> SetTimes(
                Wacs.WASI.Preview2.Filesystem.NewTimestamp ats,
                Wacs.WASI.Preview2.Filesystem.NewTimestamp mts)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                (byte[], bool),
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                Read(ulong length, ulong offset)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<ulong,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                Write(byte[] buffer, ulong offset)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.IDirectoryEntryStream,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                ReadDirectory()
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode> Sync()
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                CreateDirectoryAt(string path)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.DescriptorStat,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                StatAt(Wacs.WASI.Preview2.Filesystem.PathFlags pf,
                    string path)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                SetTimesAt(Wacs.WASI.Preview2.Filesystem.PathFlags pf,
                    string path,
                    Wacs.WASI.Preview2.Filesystem.NewTimestamp ats,
                    Wacs.WASI.Preview2.Filesystem.NewTimestamp mts)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                LinkAt(Wacs.WASI.Preview2.Filesystem.PathFlags pf,
                    string oldPath,
                    Wacs.WASI.Preview2.Filesystem.IDescriptor newDesc,
                    string newPath)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.IDescriptor,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                OpenAt(Wacs.WASI.Preview2.Filesystem.PathFlags pf,
                    string path,
                    Wacs.WASI.Preview2.Filesystem.OpenFlags of,
                    Wacs.WASI.Preview2.Filesystem.DescriptorFlags df)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<string,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                ReadlinkAt(string path)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                RemoveDirectoryAt(string path)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                RenameAt(string oldPath,
                    Wacs.WASI.Preview2.Filesystem.IDescriptor newDesc,
                    string newPath)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                SymlinkAt(string oldPath, string newPath)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.ComponentModel.Runtime.Unit,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                UnlinkFileAt(string path)
                => throw new NotImplementedException();
            public bool IsSameObject(
                Wacs.WASI.Preview2.Filesystem.IDescriptor other)
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.MetadataHashValue,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                MetadataHash()
                => throw new NotImplementedException();
            public Wacs.ComponentModel.Runtime.Result<
                Wacs.WASI.Preview2.Filesystem.MetadataHashValue,
                Wacs.WASI.Preview2.Filesystem.ErrorCode>
                MetadataHashAt(Wacs.WASI.Preview2.Filesystem.PathFlags pf,
                    string path)
                => throw new NotImplementedException();
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
