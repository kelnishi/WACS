// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Security.Cryptography;

namespace Wacs.WASI.Preview2.Random
{
    /// <summary>
    /// Default <see cref="IRandom"/> implementation backed by
    /// <see cref="RandomNumberGenerator"/> — the BCL's CSPRNG.
    /// Satisfies the spec's "at least as secure as a CSPRNG"
    /// guarantee; non-blocking; never deterministic.
    ///
    /// <para>Stateless — no per-instance fields beyond the BCL
    /// helper, so a single instance can serve every component
    /// that imports <c>wasi:random/random</c>.</para>
    /// </summary>
    public sealed class Random : IRandom
    {
        public byte[] GetRandomBytes(ulong len)
        {
            // The WIT signature takes u64 but a host buffer
            // larger than int.MaxValue can't be allocated by
            // .NET's array machinery anyway. Cap explicitly so
            // the failure mode is the same on every framework.
            if (len > int.MaxValue)
                throw new System.ArgumentOutOfRangeException(
                    nameof(len),
                    "WASI random byte counts above int.MaxValue "
                    + "are not supported by .NET array allocation.");
            var buf = new byte[(int)len];
            if (buf.Length > 0)
                RandomNumberGenerator.Fill(buf);
            return buf;
        }

        public ulong GetRandomU64()
        {
            // 8-byte CSPRNG fill → little-endian u64. Same
            // byte-stream a guest would see from the bytes-form
            // call, just unpacked.
            System.Span<byte> buf = stackalloc byte[8];
            RandomNumberGenerator.Fill(buf);
            return System.BitConverter.ToUInt64(buf);
        }
    }
}
