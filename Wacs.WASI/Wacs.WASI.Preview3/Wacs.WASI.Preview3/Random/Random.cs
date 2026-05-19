// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Security.Cryptography;

namespace Wacs.WASI.Preview3.Random
{
    /// <summary>
    /// Host interface for
    /// <c>wasi:random/random@0.3.0-rc-2026-03-15</c>.
    /// Cryptographically-secure pseudo-random bytes; never
    /// blocks, never deterministic.
    /// </summary>
    public interface IRandom
    {
        /// <summary><c>get-random-bytes: func(max-len: u64) -> list&lt;u8&gt;</c>.
        /// The implementation MAY return fewer bytes than
        /// requested (a "short read"); callers requiring exact
        /// length must loop. Implementations MUST return at
        /// least 1 byte for any max-len &gt; 0, and an empty
        /// list for max-len = 0.</summary>
        byte[] GetRandomBytes(ulong maxLen);

        /// <summary><c>get-random-u64: func() -> u64</c>.</summary>
        ulong GetRandomU64();
    }

    /// <summary>
    /// Default <see cref="IRandom"/> implementation backed by
    /// <see cref="RandomNumberGenerator"/> — the BCL's CSPRNG.
    /// Satisfies the spec's "at least as secure as a CSPRNG"
    /// guarantee. Stateless — a single instance can serve every
    /// component that imports <c>wasi:random/random</c>.
    /// </summary>
    public sealed class Random : IRandom
    {
        public byte[] GetRandomBytes(ulong maxLen)
        {
            // u64 WIT param but .NET arrays cap at int.MaxValue.
            // Cap explicitly so the failure mode is identical
            // across frameworks instead of relying on the
            // implicit `new byte[(int)maxLen]` overflow.
            if (maxLen > int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(maxLen),
                    "WASI random byte counts above int.MaxValue " +
                    "are not supported by .NET array allocation.");
            var buf = new byte[(int)maxLen];
            if (buf.Length > 0) RandomNumberGenerator.Fill(buf);
            return buf;
        }

        public ulong GetRandomU64()
        {
            Span<byte> buf = stackalloc byte[8];
            RandomNumberGenerator.Fill(buf);
            return BitConverter.ToUInt64(buf);
        }
    }
}
