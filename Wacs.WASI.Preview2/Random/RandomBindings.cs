// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Random
{
    /// <summary>
    /// Orchestrator for the three <c>wasi:random/*</c>
    /// interfaces. Constructed with the host implementations
    /// (any of which can be null to skip that interface), and
    /// dispatches each one's binding through
    /// <see cref="IBindable.BindToRuntime"/>.
    ///
    /// <para>WASIp1's <c>Wasi.cs</c> orchestrator is the model:
    /// composition over reflection, each binding site is a
    /// one-line <c>BindHostFunction&lt;Func&lt;...&gt;&gt;</c>
    /// call with a typed delegate.</para>
    /// </summary>
    public sealed class RandomBindings : IBindable
    {
        private readonly IRandom? _random;
        private readonly IInsecure? _insecureRandom;
        private readonly IInsecureSeed? _insecureSeed;

        public RandomBindings(
            IRandom? random = null,
            IInsecure? insecureRandom = null,
            IInsecureSeed? insecureSeed = null)
        {
            _random = random;
            _insecureRandom = insecureRandom;
            _insecureSeed = insecureSeed;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var alloc = new Realloc(runtime);
            if (_random != null)
                BindRandom(runtime, alloc, _random);
            if (_insecureRandom != null)
                BindInsecureRandom(runtime, alloc, _insecureRandom);
            if (_insecureSeed != null)
                BindInsecureSeed(runtime, _insecureSeed);
        }

        // wasi:random/random@0.2.3
        //   get-random-bytes: func(len: u64) -> list<u8>
        //   get-random-u64: func() -> u64
        private static void BindRandom(WasmRuntime runtime,
            Realloc alloc, IRandom impl)
        {
            const string ns = "wasi:random/random@0.2.3";

            runtime.BindHostFunction<Func<ExecContext, long>>(
                (ns, "get-random-u64"),
                _ => (long)impl.GetRandomU64());

            runtime.BindHostFunction<Action<ExecContext, long, int>>(
                (ns, "get-random-bytes"),
                (ctx, len, retArea) =>
                    WriteByteList(ctx, alloc, retArea,
                        impl.GetRandomBytes((ulong)len)));
        }

        // wasi:random/insecure@0.2.3
        //   get-insecure-random-bytes: func(len: u64) -> list<u8>
        //   get-insecure-random-u64: func() -> u64
        private static void BindInsecureRandom(WasmRuntime runtime,
            Realloc alloc, IInsecure impl)
        {
            const string ns = "wasi:random/insecure@0.2.3";

            runtime.BindHostFunction<Func<ExecContext, long>>(
                (ns, "get-insecure-random-u64"),
                _ => (long)impl.GetInsecureRandomU64());

            runtime.BindHostFunction<Action<ExecContext, long, int>>(
                (ns, "get-insecure-random-bytes"),
                (ctx, len, retArea) =>
                    WriteByteList(ctx, alloc, retArea,
                        impl.GetInsecureRandomBytes((ulong)len)));
        }

        // wasi:random/insecure-seed@0.2.3
        //   insecure-seed: func() -> tuple<u64, u64>
        // Tuple of primitives: 16-byte retArea (two u64s, align 8).
        private static void BindInsecureSeed(WasmRuntime runtime,
            IInsecureSeed impl)
        {
            const string ns = "wasi:random/insecure-seed@0.2.3";

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "insecure-seed"),
                (ctx, retArea) =>
                {
                    var (a, b) = impl.InsecureSeed();
                    var mem = ctx.Memory();
                    MemoryWriter.WriteU64LE(mem, retArea, a);
                    MemoryWriter.WriteU64LE(mem, retArea + 8, b);
                });
        }

        // Shared write path for both random + insecure: allocate
        // guest memory via cabi_realloc, copy the host byte[]
        // into it, write (ptr, len) into the retArea slot.
        private static void WriteByteList(ExecContext ctx,
            Realloc alloc, int retArea, byte[] data)
        {
            int ptr = data.Length == 0 ? 0
                : alloc.Allocate(1, data.Length);
            if (data.Length > 0)
                Array.Copy(data, 0, ctx.Memory(), ptr, data.Length);
            ctx.WriteI32LE(retArea, ptr);
            ctx.WriteI32LE(retArea + 4, data.Length);
        }
    }
}
