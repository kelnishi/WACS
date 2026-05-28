// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Random;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice B coverage:
    /// <c>wasi:random/{random, insecure, insecure-seed}@0.3.0-rc-2026-03-15</c>.
    /// </summary>
    public class RandomTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        // ---- IRandom default impl ------------------------------------

        [Fact]
        public void Random_get_random_u64_does_not_return_zero_reliably()
        {
            // Three consecutive zeros from a CSPRNG is
            // astronomically unlikely. If all three are zero
            // the BCL is broken.
            var rng = new Random.Random();
            ulong a = rng.GetRandomU64();
            ulong b = rng.GetRandomU64();
            ulong c = rng.GetRandomU64();
            Assert.True(a != 0 || b != 0 || c != 0);
        }

        [Fact]
        public void Random_get_random_bytes_returns_requested_length()
        {
            var rng = new Random.Random();
            var bytes = rng.GetRandomBytes(64);
            Assert.Equal(64, bytes.Length);
        }

        [Fact]
        public void Random_get_random_bytes_zero_length_returns_empty()
        {
            var rng = new Random.Random();
            var bytes = rng.GetRandomBytes(0);
            Assert.Empty(bytes);
        }

        [Fact]
        public void Random_get_random_bytes_rejects_oversize_request()
        {
            var rng = new Random.Random();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => rng.GetRandomBytes(((ulong)int.MaxValue) + 1));
        }

        // ---- IInsecure default impl ----------------------------------

        [Fact]
        public void InsecureRandom_get_insecure_random_bytes_returns_requested_length()
        {
            var rng = new InsecureRandom();
            var bytes = rng.GetInsecureRandomBytes(32);
            Assert.Equal(32, bytes.Length);
        }

        [Fact]
        public void InsecureRandom_get_insecure_random_u64_returns_non_zero()
        {
            // System.Random.Next() can return 0 occasionally;
            // three in a row would be a bug.
            var rng = new InsecureRandom();
            ulong a = rng.GetInsecureRandomU64();
            ulong b = rng.GetInsecureRandomU64();
            ulong c = rng.GetInsecureRandomU64();
            Assert.True(a != 0 || b != 0 || c != 0);
        }

        // ---- IInsecureSeed default impl ------------------------------

        [Fact]
        public void InsecureSeed_returns_pair_of_u64s()
        {
            var src = new InsecureSeedSource();
            var (a, b) = src.GetInsecureSeed();
            // Sanity: at least one of the pair should be non-zero
            // from a CSPRNG fill.
            Assert.True(a != 0 || b != 0);
        }

        // ---- Wire-level binding via WasiPreview3Host -----------------

        [Fact]
        public void BindToRuntime_registers_random_host_functions()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.RandomModuleName, "get-random-u64"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.RandomModuleName, "get-random-bytes"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.InsecureRandomModuleName,
                 "get-insecure-random-u64"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.InsecureRandomModuleName,
                 "get-insecure-random-bytes"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.InsecureSeedModuleName, "get-insecure-seed"),
                out _));
        }

        [Fact]
        public void InvokeGetInsecureSeed_writes_u64_pair_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var stub = new StubInsecureSeed(0xDEADBEEF12345678UL, 0x1122334455667788UL);

            const int retptr = 64;
            host.InvokeGetInsecureSeed(stub, retptr);

            var bytes = dispatcher.Memory.AsSpan(retptr, 16);
            ulong a = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0));
            ulong b = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8));
            Assert.Equal(0xDEADBEEF12345678UL, a);
            Assert.Equal(0x1122334455667788UL, b);
        }

        [Fact]
        public void InvokeGetInsecureSeed_rejects_misaligned_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new StubInsecureSeed(0, 0);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeGetInsecureSeed(stub, 12)); // not 8-aligned
            Assert.Contains("misaligned", thrown.Message);
        }

        [Fact]
        public void InvokeGetInsecureSeed_throws_when_dispatcher_unset()
        {
            var host = new WasiPreview3Host();
            var stub = new StubInsecureSeed(0, 0);
            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeGetInsecureSeed(stub, 0));
            Assert.Contains("Dispatcher", thrown.Message);
        }

        [Fact]
        public void InvokeGetRandomBytes_zero_length_writes_zero_ptr_zero_len()
        {
            // Zero-length list doesn't need cabi_realloc; the
            // ptr field is 0 (canon null) and the len field is 0.
            // This path exercises the binding without requiring
            // a real cabi_realloc export.
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var stub = new StubRandom(System.Array.Empty<byte>());
            var realloc = new Realloc(new WasmRuntime());

            const int retptr = 64;
            host.InvokeGetRandomBytes(stub, realloc, 0, retptr);

            var bytes = dispatcher.Memory.AsSpan(retptr, 8);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0));
            int len = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4));
            Assert.Equal(0, ptr);
            Assert.Equal(0, len);
        }

        [Fact]
        public void InvokeGetRandomBytes_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new StubRandom(System.Array.Empty<byte>());
            var realloc = new Realloc(new WasmRuntime());

            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeGetRandomBytes(stub, realloc, 0, 1));
            Assert.Contains("misaligned", thrown.Message);
        }

        [Fact]
        public void Host_default_construct_provides_default_random_impls()
        {
            var host = new WasiPreview3Host();
            Assert.NotNull(host.Random);
            Assert.NotNull(host.InsecureRandom);
            Assert.NotNull(host.InsecureSeed);
        }

        [Fact]
        public void Host_builder_overrides_random_threaded()
        {
            var stub = new StubRandom(new byte[] { 0xAB });
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                Random = stub,
            });
            Assert.Same(stub, host.Random);
        }

        // ---- Test stubs ----------------------------------------------

        private sealed class StubRandom : IRandom
        {
            private readonly byte[] _bytes;
            public StubRandom(byte[] bytes) { _bytes = bytes; }
            public byte[] GetRandomBytes(ulong maxLen) => _bytes;
            public ulong GetRandomU64() => 0;
        }

        private sealed class StubInsecureSeed : IInsecureSeed
        {
            private readonly ulong _a;
            private readonly ulong _b;
            public StubInsecureSeed(ulong a, ulong b) { _a = a; _b = b; }
            public (ulong, ulong) GetInsecureSeed() => (_a, _b);
        }
    }
}
