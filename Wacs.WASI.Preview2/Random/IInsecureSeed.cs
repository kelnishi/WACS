// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Random
{
    /// <summary>
    /// Host-side surface for
    /// <c>wasi:random/insecure-seed@0.2.x</c>. A single 128-bit
    /// seed value, intended to seed guest-side PRNGs.
    /// <code>
    /// interface insecure-seed {
    ///     insecure-seed: func() -&gt; tuple&lt;u64, u64&gt;;
    /// }
    /// </code>
    ///
    /// <para>The C# return type is <see cref="ValueTuple{T1, T2}"/>;
    /// the auto-binder's record-of-primitives wrapper handles
    /// tuples too since they're structs with public fields.</para>
    /// </summary>
    public interface IInsecureSeed
    {
        (ulong, ulong) InsecureSeed();
    }
}
