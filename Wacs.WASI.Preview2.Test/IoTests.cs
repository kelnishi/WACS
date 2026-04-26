// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class IoTests
    {
        [Fact]
        public void Pollable_default_Ready_returns_true()
        {
            var p = new Pollable();
            Assert.True(p.Ready());
        }

        [Fact]
        public void Pollable_default_Block_returns_immediately()
        {
            // Default impl is "always ready" — Block should
            // return without blocking. If it ever hangs, the
            // test infrastructure will fail it on timeout
            // rather than passing silently.
            var p = new Pollable();
            p.Block();
        }

        [Fact]
        public void Error_carries_message_through_ToDebugString()
        {
            var e = new Error("disk full");
            Assert.Equal("disk full", e.ToDebugString());
        }

        [Fact]
        public void Error_default_constructor_normalizes_null_message()
        {
            // Defensive: a null message argument shouldn't
            // surface as a NullReferenceException when guests
            // call to-debug-string. Norm to empty string.
            var e = new Error(null!);
            Assert.Equal("", e.ToDebugString());
        }
    }
}
