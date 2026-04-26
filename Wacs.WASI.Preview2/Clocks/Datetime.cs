// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Mirrors the WIT record from
    /// <c>wasi:clocks/wall-clock@0.2.x</c>:
    /// <code>
    /// record datetime {
    ///     seconds: u64,
    ///     nanoseconds: u32,
    /// }
    /// </code>
    /// Public mutable fields rather than properties so the
    /// binder's reflection-based field walk can read both the
    /// declared field type and its value at canon-lower
    /// emission + invoke time. The seconds field is "wall-
    /// clock seconds since the Unix epoch" per the WASI spec.
    /// </summary>
    public struct Datetime
    {
        public ulong Seconds;
        public uint Nanoseconds;
    }
}
