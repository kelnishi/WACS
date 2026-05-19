// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using SysRandom = System.Random;

namespace Wacs.WASI.Preview3.Random
{
    /// <summary>
    /// Host interface for
    /// <c>wasi:random/insecure@0.3.0-rc-2026-03-15</c>.
    /// Insecure pseudo-random data — fast, not seeded from
    /// entropy by default, NOT cryptographically secure.
    /// </summary>
    public interface IInsecure
    {
        /// <summary><c>get-insecure-random-bytes: func(max-len: u64)
        /// -> list&lt;u8&gt;</c>. Short-read semantics identical
        /// to <see cref="IRandom.GetRandomBytes"/>.</summary>
        byte[] GetInsecureRandomBytes(ulong maxLen);

        /// <summary><c>get-insecure-random-u64: func() -> u64</c>.</summary>
        ulong GetInsecureRandomU64();
    }

    /// <summary>
    /// Default <see cref="IInsecure"/> implementation backed by
    /// <see cref="SysRandom"/> — the BCL's non-cryptographic
    /// PRNG. Fast; suitable for guests that just need "some
    /// randomness" without paying the CSPRNG overhead.
    /// </summary>
    public sealed class InsecureRandom : IInsecure
    {
        // Per-instance to keep netstandard2.1 compatibility
        // (Random.Shared is .NET 6+). Not thread-safe by
        // construction — guests typically call this serially.
        private readonly SysRandom _rng = new SysRandom();

        public byte[] GetInsecureRandomBytes(ulong maxLen)
        {
            if (maxLen > int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(maxLen),
                    "Insecure random byte counts above int.MaxValue " +
                    "are not supported.");
            var buf = new byte[(int)maxLen];
            if (buf.Length > 0) _rng.NextBytes(buf);
            return buf;
        }

        public ulong GetInsecureRandomU64()
        {
            // NextInt64 is .NET 6+; combine two NextInt32() calls
            // for netstandard2.1 compatibility.
            ulong hi = (uint)_rng.Next();
            ulong lo = (uint)_rng.Next();
            return (hi << 32) | lo;
        }
    }
}
