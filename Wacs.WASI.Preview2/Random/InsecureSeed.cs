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
    /// Default <see cref="IInsecureSeed"/> implementation
    /// backed by <see cref="RandomNumberGenerator"/> — the
    /// "insecure-seed" WIT name notwithstanding, the spec
    /// recommends seeding guest PRNGs from the host's CSPRNG
    /// for unpredictability across guest restarts. Class is
    /// named <c>InsecureSeedSource</c> rather than
    /// <c>InsecureSeed</c> to avoid the C# constructor-name
    /// clash with the <see cref="IInsecureSeed.InsecureSeed"/>
    /// method.
    /// </summary>
    public sealed class InsecureSeedSource : IInsecureSeed
    {
        public (ulong, ulong) InsecureSeed()
        {
            System.Span<byte> buf = stackalloc byte[16];
            RandomNumberGenerator.Fill(buf);
            return (
                System.BitConverter.ToUInt64(buf.Slice(0, 8)),
                System.BitConverter.ToUInt64(buf.Slice(8, 8)));
        }
    }
}
