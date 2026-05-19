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

        // ---- Slice I.1: string task.return lift -----------------------

        [Fact]
        public async Task TaskReturn_string_lift_via_dispatcher_memory_round_trips()
        {
            // The binder-emitted delegate for `task.return string`
            // reads bytes from dispatcher.Memory at (ptr, len) per
            // the StringEncoding, then forwards to TaskReturn as
            // a CLR string. Exercise the path end-to-end via the
            // dispatcher's public surface.
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            d.Memory = mem;
            d.StringEncoding =
                Wacs.ComponentModel.Runtime.Parser.CanonOption.Kind.StringUtf8;

            // Plant a UTF-8 string in memory at offset 32.
            const string expected = "hello, canon-async";
            var bytes = System.Text.Encoding.UTF8.GetBytes(expected);
            bytes.CopyTo(mem.Data, 32);

            // Register a task and push it as current — the
            // delegate needs an ambient task to settle.
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                // Build the same delegate the binder would emit
                // for `task.return string` via the public
                // TryBuildDelegateForEntry surface.
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.String),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = Wacs.ComponentModel.Async.CanonAsyncBinder
                    .TryBuildDelegateForEntry(entry, d);
                Assert.NotNull(del);
                var typed = (Action<ExecContext, int, int>)del!;
                typed(null!, 32, bytes.Length);
            }
            finally
            {
                d.PopCurrentTask();
            }

            // task.return surfaces the lifted string as the host await.
            var result = await task.Completion.Task;
            Assert.Equal(expected, result);
        }

        // ---- Slice I.2: list<T> task.return lift ---------------------

        [Fact]
        public async Task TaskReturn_list_u8_lift_via_dispatcher_memory_round_trips()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            d.Memory = mem;
            // Type table: index 0 = list<u8>.
            var listOfU8 = new Wacs.ComponentModel.Runtime.Parser.ComponentListType(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U8));
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { listOfU8 };

            // Plant 5 bytes at offset 16.
            byte[] expected = { 10, 20, 30, 40, 50 };
            expected.CopyTo(mem.Data, 16);

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 16, expected.Length);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Equal(expected, (byte[])result!);
        }

        [Fact]
        public async Task TaskReturn_list_u32_lift_via_dispatcher_memory_round_trips()
        {
            var d = new AsyncDispatcher();
            var mem = MakeMemory();
            d.Memory = mem;
            var listOfU32 = new Wacs.ComponentModel.Runtime.Parser.ComponentListType(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32));
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { listOfU32 };

            // Plant 3 u32s at offset 8 (LE).
            uint[] expected = { 0x11223344, 0x55667788, 0x99AABBCC };
            for (int i = 0; i < expected.Length; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                    mem.Data.AsSpan(8 + i * 4, 4), expected[i]);

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 8, expected.Length);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Equal(expected, (uint[])result!);
        }

        // ---- Slice I.3: option<T> / result<T,E> task.return lift -----

        [Fact]
        public async Task TaskReturn_option_i32_some_lifts_value()
        {
            var d = new AsyncDispatcher();
            var optU32 = new Wacs.ComponentModel.Runtime.Parser.ComponentOptionType(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32));
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { optU32 };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // disc=1 (some), payload=42
                del(null!, 1, 42);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Equal(42, ((int?)result)!.Value);
        }

        [Fact]
        public async Task TaskReturn_option_i32_none_lifts_to_null()
        {
            var d = new AsyncDispatcher();
            var optU32 = new Wacs.ComponentModel.Runtime.Parser.ComponentOptionType(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32));
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { optU32 };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // disc=0 (none), payload ignored
                del(null!, 0, 999);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Null(result);
        }

        [Fact]
        public async Task TaskReturn_result_u32_u32_ok_lifts_isOk_true()
        {
            var d = new AsyncDispatcher();
            var resU32U32 = new Wacs.ComponentModel.Runtime.Parser.ComponentResultType(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32));
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { resU32U32 };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // disc=0 (ok), ok=7, err=99 (ignored on ok path)
                del(null!, 0, 7, 99);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var tuple = ((bool isOk, int ok, int err))result!;
            Assert.True(tuple.isOk);
            Assert.Equal(7, tuple.ok);
        }

        [Fact]
        public async Task TaskReturn_result_u32_u32_err_lifts_isOk_false()
        {
            var d = new AsyncDispatcher();
            var resU32U32 = new Wacs.ComponentModel.Runtime.Parser.ComponentResultType(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                Wacs.ComponentModel.Runtime.Parser.ComponentValType
                    .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32));
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { resU32U32 };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // disc=1 (err), ok=0, err=42
                del(null!, 1, 0, 42);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var tuple = ((bool isOk, int ok, int err))result!;
            Assert.False(tuple.isOk);
            Assert.Equal(42, tuple.err);
        }

        // ---- Slice I.4: tuple<T, T, ...> task.return lift -----------

        [Fact]
        public async Task TaskReturn_tuple_u32_u32_lifts_as_value_tuple()
        {
            var d = new AsyncDispatcher();
            var tupU32U32 = new Wacs.ComponentModel.Runtime.Parser.ComponentTupleType(
                new[]
                {
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
                { tupU32U32 };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 11, 22);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var tuple = ((int, int))result!;
            Assert.Equal((11, 22), tuple);
        }

        [Fact]
        public async Task TaskReturn_tuple_u64_u64_u64_arity3()
        {
            var d = new AsyncDispatcher();
            var tup = new Wacs.ComponentModel.Runtime.Parser.ComponentTupleType(
                new[]
                {
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U64),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U64),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U64),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { tup };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, long, long, long>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 100L, 200L, 300L);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var tuple = ((long, long, long))result!;
            Assert.Equal((100L, 200L, 300L), tuple);
        }

        [Fact]
        public async Task TaskReturn_tuple_f64_arity4()
        {
            var d = new AsyncDispatcher();
            var tup = new Wacs.ComponentModel.Runtime.Parser.ComponentTupleType(
                new[]
                {
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { tup };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, double, double, double, double>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 1.5, 2.5, 3.5, 4.5);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var tuple = ((double, double, double, double))result!;
            Assert.Equal((1.5, 2.5, 3.5, 4.5), tuple);
        }

        [Fact]
        public void TaskReturn_tuple_mixed_width_returns_null_delegate()
        {
            // tuple<u32, u64> mixes primitive widths — Slice I.4
            // doesn't support this shape. TryBuildDelegateForEntry
            // returns null; the binder skips registration.
            var d = new AsyncDispatcher();
            var tup = new Wacs.ComponentModel.Runtime.Parser.ComponentTupleType(
                new[]
                {
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U64),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { tup };

            var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
            var del = Wacs.ComponentModel.Async.CanonAsyncBinder
                .TryBuildDelegateForEntry(entry, d);
            Assert.Null(del);
        }

        [Fact]
        public void TaskReturn_string_lift_throws_when_dispatcher_memory_not_set()
        {
            // Dispatcher.Memory is null — common during the
            // bind-time/instantiate-time window. The delegate
            // throws when invoked, with a clear message.
            var d = new AsyncDispatcher();
            // No Memory assignment.
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType
                        .OfPrim(Wacs.ComponentModel.Runtime.Parser.ComponentPrim.String),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;

                var thrown = Assert.Throws<InvalidOperationException>(
                    () => del(null!, 0, 0));
                Assert.Contains("Memory", thrown.Message);
            }
            finally
            {
                d.PopCurrentTask();
            }
        }

        // ---- Slice K1: enum / flags task.return lift -----------------

        [Fact]
        public async Task TaskReturn_enum_lifts_discriminant_int()
        {
            var d = new AsyncDispatcher();
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
            {
                new Wacs.ComponentModel.Runtime.Parser.ComponentEnumType(
                    new[] { "alpha", "beta", "gamma" }),
            };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 1);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Equal(1, result);
        }

        [Fact]
        public void TaskReturn_enum_rejects_out_of_range_discriminant()
        {
            var d = new AsyncDispatcher();
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
            {
                new Wacs.ComponentModel.Runtime.Parser.ComponentEnumType(
                    new[] { "a", "b" }),
            };
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;

                var thrown = Assert.Throws<InvalidOperationException>(
                    () => del(null!, 5));
                Assert.Contains("out of range", thrown.Message);
            }
            finally { d.PopCurrentTask(); }
        }

        [Fact]
        public async Task TaskReturn_flags_lifts_bitfield_as_uint()
        {
            var d = new AsyncDispatcher();
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
            {
                new Wacs.ComponentModel.Runtime.Parser.ComponentFlagsType(
                    new[] { "read", "write", "exec" }),
            };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // read | exec = 0b101
                del(null!, 0b101);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Equal(0b101u, result);
        }

        [Fact]
        public async Task TaskReturn_flags_full_32_bits_round_trip()
        {
            var d = new AsyncDispatcher();
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 32; i++) names.Add($"f{i}");
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
            {
                new Wacs.ComponentModel.Runtime.Parser.ComponentFlagsType(names),
            };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // All bits set — valid when flag count == 32.
                del(null!, unchecked((int)0xFFFFFFFFu));
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            Assert.Equal(0xFFFFFFFFu, result);
        }

        [Fact]
        public void TaskReturn_flags_rejects_reserved_high_bits()
        {
            // 3 declared flags → validMask = 0b111. Setting bit 3
            // (0b1000) is a wire-protocol violation.
            var d = new AsyncDispatcher();
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
            {
                new Wacs.ComponentModel.Runtime.Parser.ComponentFlagsType(
                    new[] { "a", "b", "c" }),
            };
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                var thrown = Assert.Throws<InvalidOperationException>(
                    () => del(null!, 0b1000));
                Assert.Contains("reserved high bits", thrown.Message);
            }
            finally { d.PopCurrentTask(); }
        }

        [Fact]
        public void TaskReturn_flags_over_32_returns_null_delegate()
        {
            // >32 flags need multiple i32 slots — Slice K1 declines.
            var d = new AsyncDispatcher();
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 64; i++) names.Add($"f{i}");
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[]
            {
                new Wacs.ComponentModel.Runtime.Parser.ComponentFlagsType(names),
            };

            var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
            var del = Wacs.ComponentModel.Async.CanonAsyncBinder
                .TryBuildDelegateForEntry(entry, d);
            Assert.Null(del);
        }

        // ---- Slice K2: record + variant task.return lift -------------

        [Fact]
        public async Task TaskReturn_record_arity2_same_primitive_surfaces_dict()
        {
            var d = new AsyncDispatcher();
            var rec = new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "x",
                        Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32)),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "y",
                        Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32)),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { rec };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 42, 99);
            }
            finally { d.PopCurrentTask(); }

            var result = await task.Completion.Task;
            var dict = (System.Collections.Generic.IReadOnlyDictionary<string, object?>)result!;
            Assert.Equal(42, dict["x"]);
            Assert.Equal(99, dict["y"]);
        }

        [Fact]
        public async Task TaskReturn_record_arity4_f64_round_trip()
        {
            var d = new AsyncDispatcher();
            var rec = new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "a", Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64)),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "b", Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64)),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "c", Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64)),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "d", Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.F64)),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { rec };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, double, double, double, double>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 1.5, 2.5, 3.5, 4.5);
            }
            finally { d.PopCurrentTask(); }

            var dict = (System.Collections.Generic.IReadOnlyDictionary<string, object?>)
                (await task.Completion.Task)!;
            Assert.Equal(1.5, dict["a"]);
            Assert.Equal(2.5, dict["b"]);
            Assert.Equal(3.5, dict["c"]);
            Assert.Equal(4.5, dict["d"]);
        }

        [Fact]
        public void TaskReturn_record_mixed_width_returns_null_delegate()
        {
            // Mixed-width fields need a per-shape signature
            // cross-product — Slice K2 declines; Session 3
            // source-gen typed-record lifters cover this.
            var d = new AsyncDispatcher();
            var rec = new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "a", Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32)),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentRecordType.Field(
                        "b", Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U64)),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { rec };

            var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
            var del = Wacs.ComponentModel.Async.CanonAsyncBinder
                .TryBuildDelegateForEntry(entry, d);
            Assert.Null(del);
        }

        [Fact]
        public async Task TaskReturn_variant_all_unit_cases_lifts_discriminant()
        {
            var d = new AsyncDispatcher();
            var variant = new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "red", null, null),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "green", null, null),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "blue", null, null),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { variant };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                del(null!, 2);
            }
            finally { d.PopCurrentTask(); }

            Assert.Equal(2, await task.Completion.Task);
        }

        [Fact]
        public async Task TaskReturn_variant_uniform_u32_payload_lifts_tuple()
        {
            var d = new AsyncDispatcher();
            var variant = new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "tag-a",
                        Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                        null),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "tag-b",
                        Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                        null),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { variant };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                // case 1 ("tag-b"), payload = 777
                del(null!, 1, 777);
            }
            finally { d.PopCurrentTask(); }

            var tup = ((int, int))(await task.Completion.Task)!;
            Assert.Equal((1, 777), tup);
        }

        [Fact]
        public void TaskReturn_variant_mixed_payloads_returns_null_delegate()
        {
            // Mixed unit + payload, or mixed primitive payload
            // types — heterogeneous-payload variant. Session 3
            // source-gen typed-variant lifters cover this.
            var d = new AsyncDispatcher();
            var variant = new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "none", null, null),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "some",
                        Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfPrim(
                            Wacs.ComponentModel.Runtime.Parser.ComponentPrim.U32),
                        null),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { variant };

            var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
            var del = Wacs.ComponentModel.Async.CanonAsyncBinder
                .TryBuildDelegateForEntry(entry, d);
            Assert.Null(del);
        }

        [Fact]
        public void TaskReturn_variant_rejects_out_of_range_discriminant()
        {
            var d = new AsyncDispatcher();
            var variant = new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType(
                new[]
                {
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "a", null, null),
                    new Wacs.ComponentModel.Runtime.Parser.ComponentVariantType.Case(
                        "b", null, null),
                });
            d.Types = new Wacs.ComponentModel.Runtime.Parser.DefTypeEntry[] { variant };

            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            try
            {
                var entry = new Wacs.ComponentModel.Runtime.Parser.CanonTaskReturn(
                    Wacs.ComponentModel.Runtime.Parser.ComponentValType.OfRef(0),
                    System.Array.Empty<Wacs.ComponentModel.Runtime.Parser.CanonOption>());
                var del = (Action<ExecContext, int>)
                    Wacs.ComponentModel.Async.CanonAsyncBinder
                        .TryBuildDelegateForEntry(entry, d)!;
                var thrown = Assert.Throws<InvalidOperationException>(
                    () => del(null!, 99));
                Assert.Contains("out of range", thrown.Message);
            }
            finally { d.PopCurrentTask(); }
        }
    }
}
