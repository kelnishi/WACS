// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Unit tests for <see cref="ResourceHandleTable"/> — the per-
    /// component-instance handle table backing the canonical-ABI
    /// <c>canon resource.new / resource.drop / resource.rep</c>
    /// intrinsics.
    ///
    /// <para>v1 is <strong>rep-as-handle</strong> 1:1 mapping —
    /// the handle returned by New(rep) IS rep. This matches what
    /// wit-bindgen-compiled Rust guests expect (they stash rep
    /// statically and dereference handle as rep without calling
    /// [resource-rep]).</para>
    /// </summary>
    public class ResourceHandleTableTests
    {
        [Fact]
        public void New_returns_rep_as_handle()
        {
            var t = new ResourceHandleTable();
            Assert.Equal(0x10001, t.New(0x10001));
            Assert.Equal(0x10042, t.New(0x10042));
        }

        [Fact]
        public void New_rejects_zero_rep()
        {
            var t = new ResourceHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.New(0));
        }

        [Fact]
        public void New_rejects_duplicate_rep()
        {
            var t = new ResourceHandleTable();
            t.New(42);
            Assert.Throws<InvalidOperationException>(() => t.New(42));
        }

        [Fact]
        public void Rep_returns_handle_when_live()
        {
            var t = new ResourceHandleTable();
            var h = t.New(0xABCD);
            Assert.Equal(0xABCD, t.Rep(h));
        }

        [Fact]
        public void Rep_throws_on_unknown_handle()
        {
            var t = new ResourceHandleTable();
            Assert.Throws<InvalidOperationException>(() => t.Rep(99));
        }

        [Fact]
        public void Drop_returns_handle_and_removes_slot()
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
        public void Many_news_and_drops_stay_consistent()
        {
            var t = new ResourceHandleTable();
            var reps = new int[100];
            for (int i = 0; i < 100; i++) reps[i] = i + 1;   // avoid 0
            foreach (var r in reps) t.New(r);
            Assert.Equal(100, t.LiveCount);
            foreach (var r in reps) Assert.Equal(r, t.Rep(r));
            for (int i = 0; i < 100; i += 2) t.Drop(reps[i]);
            Assert.Equal(50, t.LiveCount);
            for (int i = 1; i < 100; i += 2) Assert.Equal(reps[i], t.Rep(reps[i]));
            for (int i = 0; i < 100; i += 2) Assert.False(t.Contains(reps[i]));
        }

        [Fact]
        public void Contains_true_for_live_handle_false_for_dropped()
        {
            var t = new ResourceHandleTable();
            var h = t.New(7);
            Assert.True(t.Contains(h));
            t.Drop(h);
            Assert.False(t.Contains(h));
        }
    }
}
