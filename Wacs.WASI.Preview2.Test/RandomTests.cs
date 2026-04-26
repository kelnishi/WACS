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
