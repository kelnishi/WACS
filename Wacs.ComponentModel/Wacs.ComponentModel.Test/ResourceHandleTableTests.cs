// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Unit tests for <see cref="ResourceHandleTable"/> — the per-
    /// component-instance handle table backing the canonical-ABI
    /// <c>canon resource.new / resource.drop / resource.rep</c>
    /// intrinsics.
    /// </summary>
    public class ResourceHandleTableTests
    {
        [Fact]
        public void New_assigns_increasing_handles_starting_at_1()
        {
            var t = new ResourceHandleTable();
            Assert.Equal(1, t.New(100));
            Assert.Equal(2, t.New(200));
            Assert.Equal(3, t.New(300));
        }

        [Fact]
        public void Rep_returns_stored_value()
        {
            var t = new ResourceHandleTable();
            var h = t.New(42);
            Assert.Equal(42, t.Rep(h));
        }

        [Fact]
        public void Rep_throws_on_unknown_handle()
        {
            var t = new ResourceHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.Rep(99));
        }

        [Fact]
        public void Drop_returns_rep_and_removes_slot()
        {
            var t = new ResourceHandleTable();
            var h = t.New(42);
            Assert.Equal(42, t.Drop(h));
            Assert.False(t.Contains(h));
            Assert.Equal(0, t.LiveCount);
        }

        [Fact]
        public void Drop_throws_on_invalid_handle_zero()
        {
            var t = new ResourceHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.Drop(0));
        }

        [Fact]
        public void Drop_throws_on_unknown_handle()
        {
            var t = new ResourceHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.Drop(99));
        }

        [Fact]
        public void Drop_throws_on_double_drop()
        {
            var t = new ResourceHandleTable();
            var h = t.New(42);
            t.Drop(h);
            Assert.Throws<InvalidOperationException>(() => t.Drop(h));
        }

        [Fact]
        public void New_reuses_freelist_after_drop()
        {
            var t = new ResourceHandleTable();
            var h1 = t.New(100);
            var h2 = t.New(200);
            t.Drop(h1);
            var h3 = t.New(300);
            // h3 should reuse h1's slot rather than allocate fresh.
            Assert.Equal(h1, h3);
            Assert.Equal(300, t.Rep(h3));
            Assert.Equal(200, t.Rep(h2));
        }

        [Fact]
        public void Many_news_and_drops_stay_consistent()
        {
            var t = new ResourceHandleTable();
            var handles = new int[100];
            for (int i = 0; i < 100; i++) handles[i] = t.New(i * 10);
            Assert.Equal(100, t.LiveCount);
            for (int i = 0; i < 100; i++) Assert.Equal(i * 10, t.Rep(handles[i]));
            for (int i = 0; i < 100; i += 2) t.Drop(handles[i]);
            Assert.Equal(50, t.LiveCount);
            for (int i = 1; i < 100; i += 2) Assert.Equal(i * 10, t.Rep(handles[i]));
            for (int i = 0; i < 100; i += 2) Assert.False(t.Contains(handles[i]));
        }

        [Fact]
        public void Contains_returns_true_for_live_handle_false_for_dropped()
        {
            var t = new ResourceHandleTable();
            var h = t.New(7);
            Assert.True(t.Contains(h));
            t.Drop(h);
            Assert.False(t.Contains(h));
        }
    }
}
