// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.Random;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class InsecureRandomTests
    {
        [Fact]
        public void InsecureRandom_GetInsecureRandomU64_returns_distinct_values()
        {
            var r = new InsecureRandom();
            var a = r.GetInsecureRandomU64();
            var b = r.GetInsecureRandomU64();
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void InsecureRandom_GetInsecureRandomBytes_returns_length()
        {
            var r = new InsecureRandom();
            var bytes = r.GetInsecureRandomBytes(64);
            Assert.Equal(64, bytes.Length);
        }

        [Fact]
        public void InsecureSeed_returns_distinct_high_low_pairs()
        {
            var s = new InsecureSeedSource();
            var (hi, lo) = s.InsecureSeed();
            // CSPRNG-backed; both halves should be non-zero with
            // overwhelming probability and distinct from each
            // other.
            Assert.NotEqual(0UL, hi);
            Assert.NotEqual(0UL, lo);
            Assert.NotEqual(hi, lo);
        }
    }
}
