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
        //   (type $t (func (result i64)))
        //   (import "wasi:random/random@0.2.3" "get-random-u64"
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
            // module: "wasi:random/random@0.2.3" (24 bytes)
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
                ("wasi:random/random@0.2.3", "get-random-u64"),
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
                        "wasi:random/random@0.2.3_get-random-u64")] = _ =>
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

            var instance = Activator.CreateInstance(result.ModuleClass!,
                new object[] { importsProxy, bundle })!;

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
