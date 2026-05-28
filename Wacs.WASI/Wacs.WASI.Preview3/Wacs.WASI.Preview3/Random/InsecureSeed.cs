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
    ///
    /// <para>Spec contract: "meant to be only called once. Any
    /// subsequent calls should return the same value." Implementors
    /// MUST memoize the first draw and return it on every
    /// subsequent call within the same component instance.</para>
    /// </summary>
    public interface IInsecureSeed
    {
        /// <summary><c>get-insecure-seed: func() -> tuple&lt;u64,
        /// u64&gt;</c>. Idempotent per component-instance lifetime.
        /// </summary>
        (ulong, ulong) GetInsecureSeed();
    }

    /// <summary>
    /// Default <see cref="IInsecureSeed"/> implementation backed
    /// by <see cref="RandomNumberGenerator"/>. The first call
    /// draws 128 bits of CSPRNG entropy; subsequent calls return
    /// the cached value per the spec's idempotency rule.
    /// </summary>
    public sealed class InsecureSeedSource : IInsecureSeed
    {
        private (ulong, ulong)? _seed;
        private readonly object _lock = new object();

        public (ulong, ulong) GetInsecureSeed()
        {
            if (_seed is { } s) return s;
            lock (_lock)
            {
                if (_seed is { } s2) return s2;
                Span<byte> buf = stackalloc byte[16];
                RandomNumberGenerator.Fill(buf);
                _seed = (
                    BitConverter.ToUInt64(buf.Slice(0, 8)),
                    BitConverter.ToUInt64(buf.Slice(8, 8)));
                return _seed.Value;
            }
        }
    }
}
