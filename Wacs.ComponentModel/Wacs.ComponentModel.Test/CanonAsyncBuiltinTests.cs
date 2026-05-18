// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Exercises <see cref="CanonSectionReader"/> over hand-built
    /// canon-section payloads covering every opcode in the
    /// async-ABI canon-builtin range (0x05–0x25). Per-opcode tests
    /// assert each entry's class + carried fields; a multi-entry
    /// test exercises sequential dispatch.
    ///
    /// <para>Bytes are constructed against the spec table captured
    /// in <see cref="CanonSectionReader"/>'s doc comment. Updates
    /// to the spec ripple through this file.</para>
    /// </summary>
    public class CanonAsyncBuiltinTests
    {
        // Build a canon-section payload: leading u32 entry-count
        // (single-byte LEB for counts &lt; 128) + entry bodies.
        private static byte[] WithCount(byte count, params byte[][] entries)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(count);
            foreach (var e in entries) ms.Write(e, 0, e.Length);
            return ms.ToArray();
        }

        [Fact]
        public void Decodes_task_cancel_as_marker()
        {
            var payload = WithCount(1, new byte[] { 0x05 });
            var entries = CanonSectionReader.Decode(payload);
            Assert.Single(entries);
            Assert.IsType<CanonTaskCancel>(entries[0]);
        }

        [Fact]
        public void Decodes_subtask_cancel_with_async_flag()
        {
            // 0x06 0x01 => subtask.cancel async
            var entries = CanonSectionReader.Decode(
                WithCount(1, new byte[] { 0x06, 0x01 }));
            var e = Assert.IsType<CanonSubtaskCancel>(entries[0]);
            Assert.True(e.Async);
        }

        [Fact]
        public void Decodes_subtask_cancel_without_async()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(1, new byte[] { 0x06, 0x00 }));
            var e = Assert.IsType<CanonSubtaskCancel>(entries[0]);
            Assert.False(e.Async);
        }

        [Fact]
        public void Decodes_subtask_drop_as_marker()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(1, new byte[] { 0x0d }));
            Assert.IsType<CanonSubtaskDrop>(entries[0]);
        }

        [Fact]
        public void Decodes_backpressure_set_inc_dec()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(3,
                    new byte[] { 0x08 }, new byte[] { 0x24 }, new byte[] { 0x25 }));
            Assert.Equal(CanonBackpressureOp.Kind.Set,
                Assert.IsType<CanonBackpressureOp>(entries[0]).Op);
            Assert.Equal(CanonBackpressureOp.Kind.Inc,
                Assert.IsType<CanonBackpressureOp>(entries[1]).Op);
            Assert.Equal(CanonBackpressureOp.Kind.Dec,
                Assert.IsType<CanonBackpressureOp>(entries[2]).Op);
        }

        [Fact]
        public void Decodes_task_return_with_primitive_result_and_no_options()
        {
            // 0x09 (task.return)
            // resultlist = 0x00 0x79 (varint33 = -7 = U32 primitive,
            //              7-bit two's-complement single-byte LEB)
            // opts = 0x00 (empty vec)
            var bytes = new byte[] { 0x09, 0x00, 0x79, 0x00 };
            var entries = CanonSectionReader.Decode(WithCount(1, bytes));
            var tr = Assert.IsType<CanonTaskReturn>(entries[0]);
            Assert.NotNull(tr.Result);
            Assert.True(tr.Result!.Value.IsPrimitive);
            Assert.Equal(ComponentPrim.U32, tr.Result!.Value.Prim);
            Assert.Empty(tr.Options);
        }

        [Fact]
        public void Decodes_task_return_with_empty_resultlist()
        {
            // 0x09 (task.return) resultlist = 0x01 0x00 (empty) opts = 0x00
            var entries = CanonSectionReader.Decode(
                WithCount(1, new byte[] { 0x09, 0x01, 0x00, 0x00 }));
            var tr = Assert.IsType<CanonTaskReturn>(entries[0]);
            Assert.Null(tr.Result);
            Assert.Empty(tr.Options);
        }

        [Fact]
        public void Decodes_context_get_with_primitive_valtype_and_index()
        {
            // 0x0a (context.get) v=0x79 (u32 = -7 signed-LEB) i=0x03
            var entries = CanonSectionReader.Decode(
                WithCount(1, new byte[] { 0x0a, 0x79, 0x03 }));
            var c = Assert.IsType<CanonContextOp>(entries[0]);
            Assert.Equal(CanonContextOp.Kind.Get, c.Op);
            Assert.True(c.ValType.IsPrimitive);
            Assert.Equal(ComponentPrim.U32, c.ValType.Prim);
            Assert.Equal(3u, c.Index);
        }

        [Fact]
        public void Decodes_context_set()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(1, new byte[] { 0x0b, 0x79, 0x00 }));
            var c = Assert.IsType<CanonContextOp>(entries[0]);
            Assert.Equal(CanonContextOp.Kind.Set, c.Op);
            Assert.Equal(0u, c.Index);
        }

        [Fact]
        public void Decodes_thread_yield_cancellable_and_not()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(2,
                    new byte[] { 0x0c, 0x00 },
                    new byte[] { 0x0c, 0x01 }));
            Assert.False(Assert.IsType<CanonThreadYield>(entries[0]).Cancellable);
            Assert.True(Assert.IsType<CanonThreadYield>(entries[1]).Cancellable);
        }

        [Fact]
        public void Decodes_stream_family_all_seven_ops()
        {
            // stream.new=0x0e, .read=0x0f, .write=0x10,
            // .cancel-read=0x11, .cancel-write=0x12,
            // .drop-readable=0x13, .drop-writable=0x14
            // For ops carrying opts, send an empty opts vec (0x00).
            // For ops carrying async?, send 0x01 (async present).
            // Every op carries typeidx 0x05.
            var entries = CanonSectionReader.Decode(
                WithCount(7,
                    new byte[] { 0x0e, 0x05 },                // new
                    new byte[] { 0x0f, 0x05, 0x00 },          // read t opts
                    new byte[] { 0x10, 0x05, 0x00 },          // write t opts
                    new byte[] { 0x11, 0x05, 0x01 },          // cancel-read async
                    new byte[] { 0x12, 0x05, 0x00 },          // cancel-write !async
                    new byte[] { 0x13, 0x05 },                // drop-readable
                    new byte[] { 0x14, 0x05 }));              // drop-writable

            Assert.Equal(CanonStreamOp.Kind.New,
                Assert.IsType<CanonStreamOp>(entries[0]).Op);
            var read = Assert.IsType<CanonStreamOp>(entries[1]);
            Assert.Equal(CanonStreamOp.Kind.Read, read.Op);
            Assert.NotNull(read.Options);
            Assert.Empty(read.Options!);
            var cancelRead = Assert.IsType<CanonStreamOp>(entries[3]);
            Assert.Equal(CanonStreamOp.Kind.CancelRead, cancelRead.Op);
            Assert.True(cancelRead.Async);
            var cancelWrite = Assert.IsType<CanonStreamOp>(entries[4]);
            Assert.False(cancelWrite.Async);
            Assert.Equal(5u, cancelWrite.StreamTypeIdx);
            Assert.Equal(CanonStreamOp.Kind.DropWritable,
                Assert.IsType<CanonStreamOp>(entries[6]).Op);
        }

        [Fact]
        public void Decodes_future_family_all_seven_ops()
        {
            // Same shape as stream — opcodes 0x15..0x1b.
            var entries = CanonSectionReader.Decode(
                WithCount(7,
                    new byte[] { 0x15, 0x07 },
                    new byte[] { 0x16, 0x07, 0x00 },
                    new byte[] { 0x17, 0x07, 0x00 },
                    new byte[] { 0x18, 0x07, 0x01 },
                    new byte[] { 0x19, 0x07, 0x00 },
                    new byte[] { 0x1a, 0x07 },
                    new byte[] { 0x1b, 0x07 }));
            Assert.Equal(CanonFutureOp.Kind.New,
                Assert.IsType<CanonFutureOp>(entries[0]).Op);
            Assert.Equal(7u,
                Assert.IsType<CanonFutureOp>(entries[6]).FutureTypeIdx);
            Assert.True(Assert.IsType<CanonFutureOp>(entries[3]).Async);
        }

        [Fact]
        public void Decodes_error_context_family()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(3,
                    new byte[] { 0x1c, 0x00 },   // new opts
                    new byte[] { 0x1d, 0x00 },   // debug-message opts
                    new byte[] { 0x1e }));       // drop
            Assert.Equal(CanonErrorContextOp.Kind.New,
                Assert.IsType<CanonErrorContextOp>(entries[0]).Op);
            Assert.Equal(CanonErrorContextOp.Kind.DebugMessage,
                Assert.IsType<CanonErrorContextOp>(entries[1]).Op);
            Assert.Equal(CanonErrorContextOp.Kind.Drop,
                Assert.IsType<CanonErrorContextOp>(entries[2]).Op);
        }

        [Fact]
        public void Decodes_waitable_set_family_and_waitable_join()
        {
            var entries = CanonSectionReader.Decode(
                WithCount(5,
                    new byte[] { 0x1f },                  // waitable-set.new
                    new byte[] { 0x20, 0x01, 0x00 },      // .wait cancel mem=0
                    new byte[] { 0x21, 0x00, 0x02 },      // .poll !cancel mem=2
                    new byte[] { 0x22 },                  // .drop
                    new byte[] { 0x23 }));                // waitable.join

            Assert.Equal(CanonWaitableSetOp.Kind.New,
                Assert.IsType<CanonWaitableSetOp>(entries[0]).Op);
            var wait = Assert.IsType<CanonWaitableSetOp>(entries[1]);
            Assert.Equal(CanonWaitableSetOp.Kind.Wait, wait.Op);
            Assert.True(wait.Cancellable);
            Assert.Equal(0u, wait.MemoryIdx);
            var poll = Assert.IsType<CanonWaitableSetOp>(entries[2]);
            Assert.False(poll.Cancellable);
            Assert.Equal(2u, poll.MemoryIdx);
            Assert.IsType<CanonWaitableJoin>(entries[4]);
        }

        [Fact]
        public void Rejects_threading_opcodes_with_clear_error()
        {
            // 0x26 = thread.index — deferred to the explicit-threads
            // phase per CanonSectionReader doc.
            var ex = Assert.Throws<System.FormatException>(() =>
                CanonSectionReader.Decode(WithCount(1, new byte[] { 0x26 })));
            Assert.Contains("0x26", ex.Message);
            Assert.Contains("explicit-threads", ex.Message);
        }

        [Fact]
        public void Decodes_sequential_async_entries_in_one_section()
        {
            // Sequential dispatch test — verifies decoder state
            // resets between entries and doesn't conflate sub-bytes.
            var entries = CanonSectionReader.Decode(
                WithCount(4,
                    new byte[] { 0x05 },                          // task.cancel
                    new byte[] { 0x06, 0x01 },                    // subtask.cancel async
                    new byte[] { 0x0e, 0x09 },                    // stream.new t=9
                    new byte[] { 0x09, 0x01, 0x00, 0x00 }));      // task.return empty, no opts
            Assert.Equal(4, entries.Count);
            Assert.IsType<CanonTaskCancel>(entries[0]);
            Assert.True(((CanonSubtaskCancel)entries[1]).Async);
            Assert.Equal(9u, ((CanonStreamOp)entries[2]).StreamTypeIdx);
            Assert.Null(((CanonTaskReturn)entries[3]).Result);
        }
    }
}
