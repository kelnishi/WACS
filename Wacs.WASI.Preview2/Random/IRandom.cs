// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Random
{
    /// <summary>
    /// Host-side surface for the <c>wasi:random/random@0.2.x</c>
    /// interface. Mirrors the WIT shape verbatim:
    /// <code>
    /// interface random {
    ///     get-random-bytes: func(len: u64) -&gt; list&lt;u8&gt;;
    ///     get-random-u64: func() -&gt; u64;
    /// }
    /// </code>
    ///
    /// <para>Implementations must produce data at least as
    /// cryptographically secure as a CSPRNG and must not block.
    /// See the spec at
    /// <c>Spec.Test/components/wasi-cli/wit/deps/random/random.wit</c>
    /// for the full prose contract.</para>
    /// </summary>
    public interface IRandom
    {
        /// <summary>Return <paramref name="len"/> CSPRNG bytes
        /// — fresh on every call, never deterministic.</summary>
        byte[] GetRandomBytes(ulong len);

        /// <summary>Return a CSPRNG u64. Same data shape as a
        /// 8-byte <see cref="GetRandomBytes"/> call but skips
        /// the byte-array allocation.</summary>
        ulong GetRandomU64();
    }
}
