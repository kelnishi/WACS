// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Linq;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice F coverage:
    ///
    /// <list type="bullet">
    ///   <item><see cref="AsyncLiftAdapter"/> — task lifecycle
    ///     wrapper around a synchronous wasm body invocation.</item>
    ///   <item><see cref="AsyncDispatcher"/> memory-touching ops —
    ///     stream read/write through a <see cref="MemoryInstance"/>,
    ///     error-context.new from a memory-resident string,
    ///     debug-message back out to memory.</item>
    ///   <item>Producer/consumer integration test exercising the
    ///     full dispatcher + lift adapter + memory-stream path
    ///     at the CLR test level (a full <c>.component.wasm</c>
    ///     fixture awaits wit-component output for the canon-async
    ///     pattern, see CHANGELOG).</item>
    /// </list>
    /// </summary>
    public class AsyncLiftAdapterTests
    {
        private static ContInstance MakeCont()
        {
            var store = new ContinuationStore();
            return store.Allocate((TypeIdx)0, (Delegate)(Func<int>)(() => 0));
        }

        // Synthesize a small in-memory MemoryInstance for the
        // memory-ops tests — bypasses module instantiation.
        private static MemoryInstance MakeMemory(int sizeBytes = 256)
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            var mem = new MemoryInstance(memType);
            // MemoryInstance sizes its byte[] by 64KiB pages on
            // construction; sizeBytes is just an assertion floor.
            Assert.True(mem.Data.Length >= sizeBytes);
            return mem;
        }

        // ---- AsyncLiftAdapter -----------------------------------------

        [Fact]
        public async Task InvokeAsync_void_body_implicit_returns_null()
        {
            // Block-body lambda binds the Action overload (an
            // expression-body lambda's assignment value would bind
            // the Func<T> typed overload, surfacing T not null).
            var d = new AsyncDispatcher();
            bool bodyRan = false;
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), () => { bodyRan = true; });
            Assert.True(bodyRan);
            Assert.Null(await hostTask);
            // Stack popped on exit.
            Assert.Null(d.CurrentTask);
        }

        [Fact]
        public async Task InvokeAsync_body_calls_TaskReturn_surfaces_value()
        {
            var d = new AsyncDispatcher();
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), () =>
                {
                    // Body explicitly returns via the canon op.
                    d.TaskReturn(null!, "explicit-return");
                });
            Assert.Equal("explicit-return", await hostTask);
        }

        [Fact]
        public async Task InvokeAsync_typed_overload_auto_returns_value()
        {
            var d = new AsyncDispatcher();
            var hostTask = AsyncLiftAdapter.InvokeAsync<int>(
                d, MakeCont(), () => 42);
            Assert.Equal(42, await hostTask);
        }

        [Fact]
        public async Task InvokeAsync_body_calls_TaskCancel_cancels_task()
        {
            var d = new AsyncDispatcher();
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), () => d.TaskCancel(null!));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await hostTask);
        }

        [Fact]
        public async Task InvokeAsync_body_throws_faults_returned_task()
        {
            // Body exceptions surface as a faulted Task — embedders
            // observe them via the same await they use for results.
            var d = new AsyncDispatcher();
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), () =>
                    throw new InvalidOperationException("body-bang"));
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await hostTask);
            Assert.Equal("body-bang", thrown.Message);
            Assert.Null(d.CurrentTask); // popped despite exception
        }

        [Fact]
        public void InvokeAsync_rejects_null_args()
        {
            // Argument validation is synchronous — runs before any
            // task allocation. Local void-returning wrappers force
            // xunit to pick the Action overload of Assert.Throws.
            var d = new AsyncDispatcher();
            var cont = MakeCont();
            Action body = () => { };

            void CallNullDispatcher() =>
                AsyncLiftAdapter.InvokeAsync(null!, cont, body);
            void CallNullCont() =>
                AsyncLiftAdapter.InvokeAsync(d, null!, body);
            void CallNullBody() =>
                AsyncLiftAdapter.InvokeAsync(d, cont, (Action)null!);

            Assert.Throws<ArgumentNullException>(CallNullDispatcher);
            Assert.Throws<ArgumentNullException>(CallNullCont);
            Assert.Throws<ArgumentNullException>(CallNullBody);
        }

        // ---- Memory-touching stream ops -------------------------------

        [Fact]
        public void StreamWriteFromMemory_copies_bytes_and_returns_count()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            // Plant a known sequence in memory.
            for (int i = 0; i < 8; i++) mem.Data[i] = (byte)(i + 10);
            var h = d.StreamNew(typeIdx: 0, capacity: 16);

            int written = d.StreamWriteFromMemory(h, mem, ptr: 0, length: 8);
            Assert.Equal(8, written);

            // Drain the buffer host-side and verify.
            for (int i = 0; i < 8; i++)
            {
                Assert.True(d.StreamTryRead(h, out var b));
                Assert.Equal((byte)(i + 10), b);
            }
        }

        [Fact]
        public void StreamReadToMemory_copies_bytes_and_returns_count()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            var h = d.StreamNew(typeIdx: 0, capacity: 16);
            // Plant items in the buffer through the host write.
            for (byte b = 1; b <= 4; b++) d.StreamTryWrite(h, b);

            int read = d.StreamReadToMemory(h, mem, ptr: 32, capacity: 8);
            Assert.Equal(4, read); // only 4 bytes were available
            Assert.Equal(new byte[] { 1, 2, 3, 4 },
                mem.Data.AsSpan(32, 4).ToArray());
            // Bytes past read length are untouched.
            Assert.Equal(0, mem.Data[36]);
        }

        [Fact]
        public void StreamWriteFromMemory_respects_buffer_capacity()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            for (int i = 0; i < 8; i++) mem.Data[i] = (byte)i;
            var h = d.StreamNew(typeIdx: 0, capacity: 3); // small buffer
            int written = d.StreamWriteFromMemory(h, mem, ptr: 0, length: 8);
            Assert.Equal(3, written); // backpressure kicked in
        }

        // ---- Memory-touching error-context ops ------------------------

        [Fact]
        public void ErrorContextNewFromMemory_round_trips_utf8_message()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            mem.WriteUtf8String(0, "memory-resident message");

            int h = d.ErrorContextNewFromMemory(mem, ptr: 0,
                len: (uint)System.Text.Encoding.UTF8.GetByteCount(
                    "memory-resident message"));
            Assert.Equal("memory-resident message", d.ErrorContextDebugMessage(h));
        }

        [Fact]
        public void ErrorContextDebugMessageToMemory_returns_byte_count_and_writes_when_ptr_set()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            int h = d.ErrorContextNew("hello-error");
            int expectedBytes = System.Text.Encoding.UTF8.GetByteCount("hello-error");

            // Probe (dstPtr=0) returns count without writing.
            int probedCount = d.ErrorContextDebugMessageToMemory(h, mem, dstPtr: 0);
            Assert.Equal(expectedBytes, probedCount);
            // Memory at offset 0 untouched by the probe.
            Assert.Equal(0, mem.Data[0]);

            // Actual write fills the destination.
            int writtenCount = d.ErrorContextDebugMessageToMemory(h, mem, dstPtr: 64);
            Assert.Equal(expectedBytes, writtenCount);
            var roundTripped = System.Text.Encoding.UTF8.GetString(
                mem.Data.AsSpan(64, expectedBytes));
            Assert.Equal("hello-error", roundTripped);
        }

        // ---- Producer/consumer end-to-end (CLR-level) ------------------

        [Fact]
        public async Task Producer_consumer_through_lift_adapter_round_trips_bytes()
        {
            // Models the plan's acceptance fixture:
            // producer task writes [1,2,3,4] to stream<u8>,
            // consumer task reads them. Both flow through the
            // lift adapter for task lifecycle management.
            //
            // The "core wasm body" is faked as CLR lambdas — a
            // real .component.wasm exercising this needs wit-
            // component's canon-async emit which is still in flux
            // for Preview 3 RC.
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            var producerCont = MakeCont();
            var consumerCont = MakeCont();

            // Producer creates stream, writes 4 bytes from memory.
            int streamHandle = 0;
            var producer = AsyncLiftAdapter.InvokeAsync(
                d, producerCont, () =>
                {
                    streamHandle = d.StreamNew(typeIdx: 0, capacity: 8);
                    for (byte b = 1; b <= 4; b++) mem.Data[b - 1] = b;
                    int written = d.StreamWriteFromMemory(
                        streamHandle, mem, ptr: 0, length: 4);
                    Assert.Equal(4, written);
                    d.StreamDropWritable(streamHandle);
                    d.TaskReturn(null!, written);
                });

            int produced = (int)(await producer)!;
            Assert.Equal(4, produced);

            // Consumer reads the buffered bytes back into memory.
            var consumer = AsyncLiftAdapter.InvokeAsync(
                d, consumerCont, () =>
                {
                    int read = d.StreamReadToMemory(
                        streamHandle, mem, ptr: 64, capacity: 8);
                    d.TaskReturn(null!, read);
                });

            int consumed = (int)(await consumer)!;
            Assert.Equal(4, consumed);
            Assert.Equal(new byte[] { 1, 2, 3, 4 },
                mem.Data.AsSpan(64, 4).ToArray());

            // Both tasks reached terminal state cleanly.
            Assert.Null(d.CurrentTask);
        }
    }
}
