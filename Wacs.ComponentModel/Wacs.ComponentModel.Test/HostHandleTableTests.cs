// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Harness.Runtime;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Unit tests for <see cref="HostHandleTable"/> — the host-side
    /// handle space the harness allocates over the WASM-side
    /// <see cref="Wacs.Core.Runtime.ResourceHandleTable"/>. Auto-
    /// increments handles independently of the rep values, recycles
    /// dropped slots via a freelist.
    /// </summary>
    public class HostHandleTableTests
    {
        [Fact]
        public void NewOwn_returns_fresh_handles_starting_at_1()
        {
            var t = new HostHandleTable();
            Assert.Equal(1, t.NewOwn(0x1000));
            Assert.Equal(2, t.NewOwn(0x2000));
            Assert.Equal(3, t.NewOwn(0x3000));
        }

        [Fact]
        public void NewOwn_handles_independent_of_rep_value()
        {
            var t = new HostHandleTable();
            // Same rep registered twice produces distinct handles —
            // host space is decoupled from wasm-side rep collisions.
            var h1 = t.NewOwn(0xDEAD);
            var h2 = t.NewOwn(0xDEAD);
            Assert.NotEqual(h1, h2);
        }

        [Fact]
        public void Rep_returns_stored_rep_for_live_handle()
        {
            var t = new HostHandleTable();
            var h = t.NewOwn(0xABCD);
            Assert.Equal(0xABCD, t.Rep(h));
        }

        [Fact]
        public void Rep_throws_on_unknown_handle()
        {
            var t = new HostHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.Rep(99));
        }

        [Fact]
        public void DropOwn_returns_rep_and_removes_slot()
        {
            var t = new HostHandleTable();
            var h = t.NewOwn(42);
            Assert.Equal(42, t.DropOwn(h));
            Assert.False(t.Contains(h));
            Assert.Equal(0, t.LiveCount);
        }

        [Fact]
        public void DropOwn_throws_on_invalid_handle_zero()
        {
            var t = new HostHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.DropOwn(0));
        }

        [Fact]
        public void DropOwn_throws_on_unknown_handle()
        {
            var t = new HostHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.DropOwn(99));
        }

        [Fact]
        public void DropOwn_throws_on_double_drop()
        {
            var t = new HostHandleTable();
            var h = t.NewOwn(42);
            t.DropOwn(h);
            Assert.Throws<InvalidOperationException>(() => t.DropOwn(h));
        }

        [Fact]
        public void Freelist_recycles_dropped_handles()
        {
            var t = new HostHandleTable();
            var h1 = t.NewOwn(100);
            var h2 = t.NewOwn(200);
            t.DropOwn(h1);
            // Next NewOwn should reuse h1 from the freelist before
            // bumping the counter — keeps the handle space dense.
            var h3 = t.NewOwn(300);
            Assert.Equal(h1, h3);
            // And the rep behind h3 is now 300, not 100 — slot got
            // overwritten, no stale lookup.
            Assert.Equal(300, t.Rep(h3));
            // h2 untouched.
            Assert.Equal(200, t.Rep(h2));
        }

        [Fact]
        public void Many_news_and_drops_stay_consistent()
        {
            var t = new HostHandleTable();
            var handles = new int[100];
            for (int i = 0; i < 100; i++)
                handles[i] = t.NewOwn(0x10000 + i);
            Assert.Equal(100, t.LiveCount);
            for (int i = 0; i < 100; i++)
                Assert.Equal(0x10000 + i, t.Rep(handles[i]));
            for (int i = 0; i < 100; i += 2)
                t.DropOwn(handles[i]);
            Assert.Equal(50, t.LiveCount);
            for (int i = 1; i < 100; i += 2)
                Assert.Equal(0x10000 + i, t.Rep(handles[i]));
            for (int i = 0; i < 100; i += 2)
                Assert.False(t.Contains(handles[i]));
        }

        [Fact]
        public void Contains_tracks_lifetime_correctly()
        {
            var t = new HostHandleTable();
            var h = t.NewOwn(7);
            Assert.True(t.Contains(h));
            t.DropOwn(h);
            Assert.False(t.Contains(h));
        }
    }
}
