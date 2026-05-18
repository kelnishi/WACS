// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Central API surface for the Component Model async ABI runtime.
    /// One instance per <see cref="Wacs.ComponentModel.Runtime.ComponentInstance"/>
    /// — holds the per-component handle spaces for tasks, subtasks,
    /// waitable-sets, streams, futures, and error contexts; exposes
    /// methods named after the canon-async builtins that the
    /// interpreter and transpiler both call.
    ///
    /// <para><b>Engine symmetry:</b> the dispatcher's public method
    /// shapes are deliberately stable — the interpreter dispatches
    /// canon entries directly through these methods, and the
    /// transpiler emits CIL <c>callvirt</c> sites against the same
    /// methods. Two engines, one dispatcher.</para>
    ///
    /// <para><b>What's implemented in Slice C:</b> handle allocation /
    /// drop for streams, futures, error contexts, and waitable
    /// sets; non-blocking stream and future I/O; basic subtask
    /// management; waitable-set membership. These are the canon
    /// ops that don't require suspending the current task body.</para>
    ///
    /// <para><b>What's stubbed for Slice D:</b> task lifecycle
    /// (<c>task.return</c>, <c>task.cancel</c>), backpressure,
    /// per-task context slots, <c>thread.yield</c>, waitable-set
    /// wait/poll, and the suspend-driven stream/future blocking
    /// reads. Each throws <see cref="NotImplementedException"/>
    /// with a clear "Slice D" message — the contract is pinned,
    /// the dispatch through <see cref="IContinuationContext"/>
    /// lands when the integration with the interpreter loop is
    /// wired.</para>
    /// </summary>
    public sealed class AsyncDispatcher
    {
        // Streams / futures / error-contexts erase to object at the
        // table level — the dispatcher's typed methods take/return
        // the concrete element shape (byte, etc.) and cast on the
        // way in/out. The wire surface is handles either way;
        // typing is enforced by the canon-ABI lift/lower layer
        // surrounding the dispatcher call.
        public AsyncHandleTable<ComponentTask> Tasks { get; } =
            new AsyncHandleTable<ComponentTask>();

        public AsyncHandleTable<ComponentSubtask> Subtasks { get; } =
            new AsyncHandleTable<ComponentSubtask>();

        public AsyncHandleTable<ComponentWaitableSet> WaitableSets { get; } =
            new AsyncHandleTable<ComponentWaitableSet>();

        public AsyncHandleTable<object> Streams { get; } =
            new AsyncHandleTable<object>();

        public AsyncHandleTable<object> Futures { get; } =
            new AsyncHandleTable<object>();

        public AsyncHandleTable<string> ErrorContexts { get; } =
            new AsyncHandleTable<string>();

        // ---- Task lifecycle (Slice D — current-task tracking) ----------

        /// <summary><c>canon task.return rs opts</c> — settle the
        /// ambient task's completion with the lifted result. Needs
        /// current-task tracking, lands in Slice D.</summary>
        public void TaskReturn(IContinuationContext ctx, object? result)
        {
            throw new NotImplementedException(
                "task.return needs current-task tracking (Slice D).");
        }

        /// <summary><c>canon task.cancel</c> — transition the ambient
        /// task to <see cref="ComponentTaskState.Cancelled"/>.
        /// Slice D wires this.</summary>
        public void TaskCancel(IContinuationContext ctx)
        {
            throw new NotImplementedException(
                "task.cancel needs current-task tracking (Slice D).");
        }

        /// <summary>Register a freshly-allocated task with the
        /// dispatcher. Called by the canon-lift adapter when an
        /// async export entry runs. Slice D fills in the
        /// current-task push on body entry.</summary>
        public ComponentTask RegisterTask(ContInstance continuation)
        {
            ComponentTask? created = null;
            Tasks.Allocate(handle =>
            {
                created = new ComponentTask(handle, continuation);
                return created;
            });
            return created!;
        }

        // ---- Subtask -----------------------------------------------------

        /// <summary><c>canon subtask.cancel async?</c>. Slice D wires
        /// the cancel-propagation semantics; the table-side drop
        /// is implemented today.</summary>
        public void SubtaskCancel(int subtaskHandle, bool asyncFlag)
        {
            throw new NotImplementedException(
                "subtask.cancel cancel propagation lands in Slice D.");
        }

        /// <summary><c>canon subtask.drop</c> — release the subtask
        /// handle. The child task itself is owned through its own
        /// task-table slot; dropping the subtask handle just severs
        /// the parent relationship.</summary>
        public bool SubtaskDrop(int subtaskHandle) =>
            Subtasks.Drop(subtaskHandle) != null;

        // ---- Stream (byte-typed; Slice D generalizes) -------------------

        /// <summary><c>canon stream.new t</c> — allocate a fresh
        /// stream<u8> backed by a bounded <see cref="StreamBuffer{T}"/>.
        /// The <paramref name="typeIdx"/> identifies the element
        /// type at the component-model level; Slice C concrete
        /// implementation hard-codes byte buffers (the producer/
        /// consumer fixture's <c>stream&lt;u8&gt;</c>). Slice D
        /// generalizes to other element widths.</summary>
        public int StreamNew(int typeIdx, int capacity = 64)
        {
            var buf = new StreamBuffer<byte>(capacity);
            return Streams.Allocate(buf);
        }

        /// <summary>Non-blocking write of a byte to the stream.
        /// Returns false when the buffer is at capacity — backpressure.
        /// Slice D adds the suspend-on-full variant.</summary>
        public bool StreamTryWrite(int streamHandle, byte value)
        {
            var raw = Streams.Get(streamHandle)
                ?? throw new InvalidOperationException(
                    $"stream.write: handle {streamHandle} is not allocated.");
            var buf = raw as StreamBuffer<byte>
                ?? throw new InvalidOperationException(
                    $"stream.write: handle {streamHandle} is not a byte stream.");
            return buf.TryWrite(value);
        }

        /// <summary>Non-blocking read of a byte. Returns false when
        /// the buffer is empty.</summary>
        public bool StreamTryRead(int streamHandle, out byte value)
        {
            value = 0;
            var raw = Streams.Get(streamHandle);
            if (raw is not StreamBuffer<byte> buf) return false;
            return buf.TryRead(out value);
        }

        /// <summary><c>canon stream.cancel-read t async?</c>. Slice D
        /// implements the cooperative-cancel handshake; for now,
        /// surfaces as a no-op returning the buffer's drained
        /// status.</summary>
        public bool StreamCancelRead(int streamHandle, bool asyncFlag)
        {
            throw new NotImplementedException(
                "stream.cancel-read cooperative cancel lands in Slice D.");
        }

        /// <summary><c>canon stream.cancel-write t async?</c>. Slice D.</summary>
        public bool StreamCancelWrite(int streamHandle, bool asyncFlag)
        {
            throw new NotImplementedException(
                "stream.cancel-write cooperative cancel lands in Slice D.");
        }

        /// <summary><c>canon stream.drop-readable t</c> — drop the
        /// reader half of the stream handle. Producer can still
        /// write, but pending and future reads observe completion.</summary>
        public bool StreamDropReadable(int streamHandle)
        {
            var raw = Streams.Get(streamHandle);
            if (raw is not StreamBuffer<byte> buf) return false;
            // Closing the writer side surfaces a clean EOS on the
            // reader; the table entry stays until drop-writable
            // also fires (release both halves).
            buf.Complete();
            return true;
        }

        /// <summary><c>canon stream.drop-writable t</c> — drop the
        /// writer half. Closes the channel so the reader observes
        /// end-of-stream once the buffer drains. Releases the table
        /// slot.</summary>
        public bool StreamDropWritable(int streamHandle)
        {
            if (Streams.Get(streamHandle) is StreamBuffer<byte> buf)
                buf.Complete();
            return Streams.Drop(streamHandle) != null;
        }

        // ---- Future ------------------------------------------------------

        /// <summary><c>canon future.new t</c> — allocate a fresh
        /// future cell. Element type is <c>object</c> (typed
        /// boxing); Slice D generalizes typed cells.</summary>
        public int FutureNew(int typeIdx)
        {
            var cell = new FutureCell<object?>();
            return Futures.Allocate(cell);
        }

        /// <summary><c>canon future.write t opts</c> — single-shot
        /// write. Returns false on double-write (matching the spec's
        /// trap behavior — caller turns false into a trap).</summary>
        public bool FutureWrite(int futureHandle, object? value)
        {
            if (Futures.Get(futureHandle) is not FutureCell<object?> cell)
                return false;
            return cell.TrySetResult(value);
        }

        /// <summary>Returns the future's underlying
        /// <see cref="Task{TResult}"/> so the host can <c>await</c>
        /// it. Slice D wires the wasm-side <c>future.read</c> through
        /// suspend instead of returning the Task directly.</summary>
        public Task<object?> FutureReadAsync(int futureHandle)
        {
            if (Futures.Get(futureHandle) is not FutureCell<object?> cell)
                throw new InvalidOperationException(
                    $"future.read: handle {futureHandle} is not allocated.");
            return cell.Task;
        }

        /// <summary><c>canon future.cancel-read t async?</c>. Slice D.</summary>
        public bool FutureCancelRead(int futureHandle, bool asyncFlag)
        {
            throw new NotImplementedException(
                "future.cancel-read cooperative cancel lands in Slice D.");
        }

        /// <summary><c>canon future.cancel-write t async?</c>. Slice D.</summary>
        public bool FutureCancelWrite(int futureHandle, bool asyncFlag)
        {
            throw new NotImplementedException(
                "future.cancel-write cooperative cancel lands in Slice D.");
        }

        /// <summary><c>canon future.drop-readable t</c>. Cancels any
        /// pending reader and drops the handle.</summary>
        public bool FutureDropReadable(int futureHandle)
        {
            if (Futures.Get(futureHandle) is FutureCell<object?> cell)
                cell.TrySetCanceled();
            return Futures.Drop(futureHandle) != null;
        }

        /// <summary><c>canon future.drop-writable t</c>. Same as
        /// drop-readable at this slice; full single-direction
        /// semantics land in Slice D.</summary>
        public bool FutureDropWritable(int futureHandle) =>
            FutureDropReadable(futureHandle);

        // ---- Error context -----------------------------------------------

        /// <summary><c>canon error-context.new opts</c> — allocate an
        /// error-context handle carrying the supplied debug message.
        /// Lift of the message bytes happens at the canon-ABI boundary
        /// before reaching here.</summary>
        public int ErrorContextNew(string debugMessage) =>
            ErrorContexts.Allocate(debugMessage);

        /// <summary><c>canon error-context.debug-message opts</c> —
        /// retrieve the message string for an allocated handle.</summary>
        public string ErrorContextDebugMessage(int errorContextHandle)
        {
            var msg = ErrorContexts.Get(errorContextHandle)
                ?? throw new InvalidOperationException(
                    $"error-context.debug-message: handle {errorContextHandle} not allocated.");
            return msg;
        }

        /// <summary><c>canon error-context.drop</c>. Releases the handle.</summary>
        public bool ErrorContextDrop(int errorContextHandle) =>
            ErrorContexts.Drop(errorContextHandle) != null;

        // ---- Waitable set ------------------------------------------------

        /// <summary><c>canon waitable-set.new</c>.</summary>
        public int WaitableSetNew() =>
            WaitableSets.Allocate(handle => new ComponentWaitableSet(handle));

        /// <summary><c>canon waitable-set.wait cancel? memidx</c> —
        /// suspend until any member of the set reaches a deliverable
        /// state. Needs <see cref="IContinuationContext"/> integration
        /// for the suspend mechanic. Slice D.</summary>
        public void WaitableSetWait(
            IContinuationContext ctx, int waitableSetHandle,
            int memoryIdx, bool cancellable)
        {
            throw new NotImplementedException(
                "waitable-set.wait suspend integration lands in Slice D.");
        }

        /// <summary><c>canon waitable-set.poll cancel? memidx</c> —
        /// non-blocking check for any deliverable member. Slice D.</summary>
        public void WaitableSetPoll(
            IContinuationContext ctx, int waitableSetHandle,
            int memoryIdx, bool cancellable)
        {
            throw new NotImplementedException(
                "waitable-set.poll lands in Slice D.");
        }

        /// <summary><c>canon waitable-set.drop</c>.</summary>
        public bool WaitableSetDrop(int waitableSetHandle) =>
            WaitableSets.Drop(waitableSetHandle) != null;

        /// <summary><c>canon waitable.join</c> — adds the current
        /// waitable to the set. The "current waitable-set context"
        /// is needed; Slice D wires it.</summary>
        public void WaitableJoin(int waitableSetHandle, int waitableHandle)
        {
            var ws = WaitableSets.Get(waitableSetHandle)
                ?? throw new InvalidOperationException(
                    $"waitable.join: waitable-set {waitableSetHandle} not allocated.");
            ws.Join(waitableHandle);
        }

        // ---- Backpressure (Slice D — ambient state machine) -------------

        public void BackpressureSet() =>
            throw new NotImplementedException(
                "backpressure.set ambient state lands in Slice D.");

        public void BackpressureInc() =>
            throw new NotImplementedException(
                "backpressure.inc ambient state lands in Slice D.");

        public void BackpressureDec() =>
            throw new NotImplementedException(
                "backpressure.dec ambient state lands in Slice D.");

        // ---- Context (Slice D — per-task context slots) -----------------

        public Value ContextGet(int slotIdx) =>
            throw new NotImplementedException(
                "context.get needs per-task slots (Slice D).");

        public void ContextSet(int slotIdx, Value value) =>
            throw new NotImplementedException(
                "context.set needs per-task slots (Slice D).");

        // ---- Thread.yield (Slice D — suspend integration) ---------------

        public void ThreadYield(IContinuationContext ctx, bool cancellable) =>
            throw new NotImplementedException(
                "thread.yield suspend integration lands in Slice D.");
    }
}
