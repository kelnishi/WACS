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
    /// <c>wasi:random/insecure-seed@0.3.0-rc-2026-03-15</c>.
    /// Returns a 128-bit value the guest uses to seed its own
    /// hash-map DoS protection. "Insecure" here is the WIT
    /// nomenclature — the spec actually recommends seeding from
    /// the host's CSPRNG for unpredictability across guest
    /// restarts, which is what the default impl does.
    /// </summary>
    public interface IInsecureSeed
    {
        /// <summary><c>get-insecure-seed: func() -> tuple&lt;u64,
        /// u64&gt;</c>.</summary>
        (ulong, ulong) GetInsecureSeed();
    }

    /// <summary>
    /// Default <see cref="IInsecureSeed"/> implementation backed
    /// by <see cref="RandomNumberGenerator"/>. Class is named
    /// <c>InsecureSeedSource</c> rather than <c>InsecureSeed</c>
    /// to avoid the C# constructor-name clash with
    /// <see cref="IInsecureSeed.GetInsecureSeed"/>.
    /// </summary>
    public sealed class InsecureSeedSource : IInsecureSeed
    {
        public (ulong, ulong) GetInsecureSeed()
        {
            Span<byte> buf = stackalloc byte[16];
            RandomNumberGenerator.Fill(buf);
            return (
                BitConverter.ToUInt64(buf.Slice(0, 8)),
                BitConverter.ToUInt64(buf.Slice(8, 8)));
        }
    }
}
