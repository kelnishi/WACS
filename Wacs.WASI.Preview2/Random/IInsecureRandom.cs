// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Random
{
    /// <summary>
    /// Host-side surface for <c>wasi:random/insecure@0.2.x</c>.
    /// Same shape as <see cref="IRandom"/> but explicitly
    /// non-cryptographic — fast pseudo-randomness for non-
    /// security-sensitive use cases (sampling, jitter,
    /// non-secure GUIDs). Implementations may use any PRNG.
    /// <code>
    /// interface insecure {
    ///     get-insecure-random-bytes: func(len: u64) -&gt; list&lt;u8&gt;;
    ///     get-insecure-random-u64: func() -&gt; u64;
    /// }
    /// </code>
    /// </summary>
    public interface IInsecureRandom
    {
        byte[] GetInsecureRandomBytes(ulong len);
        ulong GetInsecureRandomU64();
    }
}
