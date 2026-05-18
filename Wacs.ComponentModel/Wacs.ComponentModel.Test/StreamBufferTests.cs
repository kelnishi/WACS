// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Coverage for <see cref="StreamBuffer{T}"/> and
    /// <see cref="FutureCell{T}"/> — the Phase 3 Slice B data
    /// plane. Dispatcher wiring (Slice C) consumes these.
    /// </summary>
    public class StreamBufferTests
    {
        [Fact]
        public void Stream_constructor_rejects_zero_capacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StreamBuffer<byte>(0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StreamBuffer<byte>(-1));
        }

        [Fact]
        public void Stream_round_trips_values_within_capacity()
        {
            var sb = new StreamBuffer<byte>(capacity: 4);
            Assert.True(sb.TryWrite(1));
            Assert.True(sb.TryWrite(2));
            Assert.True(sb.TryWrite(3));
            Assert.True(sb.TryRead(out var a)); Assert.Equal(1, a);
            Assert.True(sb.TryRead(out var b)); Assert.Equal(2, b);
            Assert.True(sb.TryRead(out var c)); Assert.Equal(3, c);
            Assert.False(sb.TryRead(out _));
        }

        [Fact]
        public void Stream_TryWrite_returns_false_when_full()
        {
            // capacity=2 — the third write hits backpressure.
            var sb = new StreamBuffer<int>(capacity: 2);
            Assert.True(sb.TryWrite(10));
            Assert.True(sb.TryWrite(20));
            Assert.False(sb.TryWrite(30));
        }

        [Fact]
        public async Task Stream_WriteAsync_resumes_when_reader_drains()
        {
            // Backpressure: WriteAsync over a full buffer suspends
            // until a read frees a slot.
            var sb = new StreamBuffer<int>(capacity: 1);
            Assert.True(sb.TryWrite(42));

            var writeTask = sb.WriteAsync(99).AsTask();
            Assert.False(writeTask.IsCompleted);

            Assert.True(sb.TryRead(out var first));
            Assert.Equal(42, first);

            // Now there's space — the suspended writer resumes.
            await writeTask;
            Assert.True(sb.TryRead(out var second));
            Assert.Equal(99, second);
        }

        [Fact]
        public async Task Stream_AsAsyncEnumerable_drains_buffer_in_order()
        {
            var sb = new StreamBuffer<int>(capacity: 8);
            sb.TryWrite(1); sb.TryWrite(2); sb.TryWrite(3);
            sb.Complete();

            var collected = new List<int>();
            await foreach (var x in sb.AsAsyncEnumerable()) collected.Add(x);
            Assert.Equal(new[] { 1, 2, 3 }, collected);
        }

        [Fact]
        public void Stream_Complete_signals_IsCompleted_after_drain()
        {
            var sb = new StreamBuffer<int>(2);
            sb.TryWrite(7);
            sb.Complete();
            // Pending items still readable after Complete.
            Assert.True(sb.TryRead(out var x));
            Assert.Equal(7, x);
        }

        [Fact]
        public async Task Future_Task_completes_on_TrySetResult()
        {
            var f = new FutureCell<int>();
            Assert.False(f.IsCompleted);
            Assert.True(f.TrySetResult(123));
            Assert.True(f.IsCompleted);
            Assert.Equal(123, await f.Task);
        }

        [Fact]
        public void Future_TrySetResult_rejects_second_write()
        {
            // single-write spec semantics
            var f = new FutureCell<string>();
            Assert.True(f.TrySetResult("first"));
            Assert.False(f.TrySetResult("second"));
        }

        [Fact]
        public async Task Future_TrySetException_faults_task()
        {
            var f = new FutureCell<int>();
            var ex = new InvalidOperationException("boom");
            Assert.True(f.TrySetException(ex));
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await f.Task);
            Assert.Equal("boom", thrown.Message);
        }

        [Fact]
        public async Task Future_TrySetCanceled_cancels_task()
        {
            var f = new FutureCell<int>();
            Assert.True(f.TrySetCanceled());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await f.Task);
        }
    }
}
