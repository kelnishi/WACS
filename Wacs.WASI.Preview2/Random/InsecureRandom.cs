// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using SysRandom = System.Random;

namespace Wacs.WASI.Preview2.Random
{
    /// <summary>
    /// Default <see cref="IInsecureRandom"/> implementation
    /// backed by <see cref="System.Random"/> — the BCL's
    /// non-cryptographic Mersenne Twister. Fast, not seeded
    /// from entropy by default, suitable for guests that just
    /// need "some randomness" without paying the CSPRNG
    /// overhead.
    /// </summary>
    public sealed class InsecureRandom : IInsecureRandom
    {
        // Random.Shared is .NET 6+; netstandard2.1 needs a
        // per-instance RNG. Marked as readonly to make the
        // intent explicit even though System.Random isn't
        // thread-safe — guests typically call this serially.
        private readonly SysRandom _rng = new SysRandom();

        public byte[] GetInsecureRandomBytes(ulong len)
        {
            if (len > int.MaxValue)
                throw new System.ArgumentOutOfRangeException(
                    nameof(len), "Insecure random byte counts above "
                    + "int.MaxValue are not supported.");
            var buf = new byte[(int)len];
            if (buf.Length > 0) _rng.NextBytes(buf);
            return buf;
        }

        public ulong GetInsecureRandomU64()
        {
            // System.Random.NextInt64 is .NET 6+; combine two
            // NextInt32() calls for netstandard2.1 compatibility.
            ulong hi = (uint)_rng.Next();
            ulong lo = (uint)_rng.Next();
            return (hi << 32) | lo;
        }
    }
}
