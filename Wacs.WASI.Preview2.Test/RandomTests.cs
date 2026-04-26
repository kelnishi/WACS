// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Random;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// End-to-end tests of the wasi:random/random host binding —
    /// fixture imports the WASI interface, host satisfies it via
    /// the auto-binder, components see real CSPRNG bytes through
    /// the call chain.
    /// </summary>
    public class RandomTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [Fact]
        public void Random_GetRandomU64_returns_nonzero_csprng_bytes()
        {
            // The default Random impl uses RandomNumberGenerator
            // (CSPRNG). Two consecutive calls should differ — the
            // probability of a CSPRNG returning the same u64
            // twice in a row is 2^-64, well past any reasonable
            // false-positive bound.
            var random = new Random.Random();
            var a = random.GetRandomU64();
            var b = random.GetRandomU64();
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Random_GetRandomBytes_returns_requested_length()
        {
            var random = new Random.Random();
            var bytes = random.GetRandomBytes(32);
            Assert.Equal(32, bytes.Length);
        }

        [Fact]
        public void BindWasiInstance_wires_random_bytes_through_canon_lower()
        {
            // wasi-random-bytes-component imports
            // get-random-bytes(len: u64) -> list<u8>. Multi-core-
            // module path: wit-component generates 3 modules
            // (adapter + post-return shim + user); the
            // ComponentInstance Phase 3 v0 trace finds the user
            // module via canon-lift's CoreFuncIdx → alias →
            // core-instance → core-module chain, instantiates
            // just that one, and binds the canon-lower wrapper
            // under the user module's import name. The wrapper
            // takes (len, retAreaPtr) on the wasm stack, calls
            // Random.GetRandomBytes(len), allocates guest memory
            // via cabi_realloc, copies bytes, writes (dataPtr,
            // count) at retAreaPtr.
            //
            // Stub the host with a deterministic generator so
            // we can assert specific byte values.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-random-bytes-component", "rb.component.wasm"));
            var stub = new DeterministicRandom();
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:random/random@0.2.3", stub));

            var result = (byte[])ci.Invoke("fetch", 5UL)!;
            // Stub generates byte i = (byte)i for i in [0..len),
            // so 5 bytes = {0, 1, 2, 3, 4}.
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, result);
        }

        /// <summary>Predictable byte stream for asserting on
        /// canon-lower mechanics — produces (byte)i at index i
        /// instead of CSPRNG output.</summary>
        private sealed class DeterministicRandom : IRandom
        {
            public byte[] GetRandomBytes(ulong len)
            {
                var buf = new byte[(int)len];
                for (int i = 0; i < buf.Length; i++) buf[i] = (byte)i;
                return buf;
            }
            public ulong GetRandomU64() => 0;
        }

        [Fact]
        public void BindWasiInstance_wires_random_u64_through_runtime()
        {
            // Auto-binder walks the Random impl, picks
            // GetRandomU64 (primitive return — passes the
            // primitive-only filter), kebab-cases to
            // "get-random-u64", and registers it under the
            // WASI namespace. ComponentInstance.Invoke then sees
            // a real CSPRNG-backed call.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-random-u64-component", "rand.component.wasm"));
            var random = new Random.Random();
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:random/random@0.2.3", random));
            // Two calls; both non-zero is overwhelmingly likely.
            // Just check the call chain works — the value is
            // CSPRNG-driven so we can't assert specifics.
            var v1 = (ulong)ci.Invoke("pick")!;
            var v2 = (ulong)ci.Invoke("pick")!;
            Assert.NotEqual(v1, v2);
        }
    }
}
