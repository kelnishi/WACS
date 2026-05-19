// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Types;

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

        // Wrapper around a stream buffer that tracks the drop state
        // of each half. Spec: stream<T> has separate readable and
        // writable halves; drop-writable closes the writer but the
        // reader can still drain pending items; drop-readable
        // signals the writer no further reads will arrive. The
        // handle slot is released only after BOTH halves are dropped.
        internal sealed class StreamSlot
        {
            public StreamBuffer<byte> Buffer = null!;
            public bool WriterDropped;
            public bool ReaderDropped;
        }

        public AsyncHandleTable<object> Futures { get; } =
            new AsyncHandleTable<object>();

        public AsyncHandleTable<string> ErrorContexts { get; } =
            new AsyncHandleTable<string>();

        // ---- Current-task stack ----------------------------------------
        //
        // The "ambient task" is the task whose body is currently
        // executing on the wasm stack. The lift adapter (Slice E)
        // pushes a fresh task on body entry and pops on
        // return/cancel/exception. Canon ops that reference the
        // current task (task.return, task.cancel, context.get/set)
        // peek the top.

        private readonly Stack<ComponentTask> _currentTasks =
            new Stack<ComponentTask>();

        /// <summary>The task whose body is currently executing, or
        /// <c>null</c> when none.</summary>
        public ComponentTask? CurrentTask =>
            _currentTasks.Count > 0 ? _currentTasks.Peek() : null;

        /// <summary>Lift-adapter entry point: push a freshly-created
        /// task as the ambient task. Transitions
        /// <see cref="ComponentTaskState.Starting"/> →
        /// <see cref="ComponentTaskState.Started"/>.</summary>
        public void PushCurrentTask(ComponentTask task)
        {
            task.State = ComponentTaskState.Started;
            _currentTasks.Push(task);
        }

        /// <summary>Lift-adapter exit point: pop the ambient task.
        /// Returns the popped task or null if the stack was empty.</summary>
        public ComponentTask? PopCurrentTask() =>
            _currentTasks.Count > 0 ? _currentTasks.Pop() : null;

        // ---- Backpressure ----------------------------------------------
        //
        // A monotone counter the embedder consults to gate new task
        // creation. Set clears, Inc/Dec adjust. The state is
        // process-wide for the component instance — not per-task —
        // which matches the spec's "backpressure is an ambient
        // flag, not a per-call argument" stance.

        private int _backpressureLevel;

        /// <summary>Current backpressure level. Embedders consult
        /// this before lifting new tasks; a positive level means
        /// the component is asking callers to slow down.</summary>
        public int BackpressureLevel => _backpressureLevel;

        // ---- Task lifecycle --------------------------------------------

        /// <summary><c>canon task.return rs opts</c> — settle the
        /// ambient task's completion with the lifted result.
        /// Caller passes the already-lifted CLR object (the canon-
        /// ABI lift converted the wasm-side value before reaching
        /// here). Body continues to its natural return after this
        /// call; the lift adapter pops the task on body exit.</summary>
        public void TaskReturn(IContinuationContext ctx, object? result)
        {
            var task = CurrentTask
                ?? throw new InvalidOperationException(
                    "task.return called outside an active task body.");
            if (task.State != ComponentTaskState.Started)
                throw new InvalidOperationException(
                    $"task.return: task is in state {task.State}, expected Started.");
            task.State = ComponentTaskState.Returned;
            task.Completion.TrySetResult(result);
        }

        /// <summary><c>canon task.cancel</c> — transition the ambient
        /// task to <see cref="ComponentTaskState.Cancelled"/>. The
        /// body continues to its natural exit after this call —
        /// like <see cref="TaskReturn"/> the lift adapter handles
        /// the pop.</summary>
        public void TaskCancel(IContinuationContext ctx)
        {
            var task = CurrentTask
                ?? throw new InvalidOperationException(
                    "task.cancel called outside an active task body.");
            if (task.State != ComponentTaskState.Started)
                throw new InvalidOperationException(
                    $"task.cancel: task is in state {task.State}, expected Started.");
            task.State = ComponentTaskState.Cancelled;
            task.Completion.TrySetCanceled();
        }

        /// <summary>Mark the ambient task as failed and fault its
        /// completion. Called by the lift adapter when an exception
        /// escapes the body without being caught.</summary>
        public void TaskFail(Exception exception)
        {
            var task = CurrentTask
                ?? throw new InvalidOperationException(
                    "TaskFail called outside an active task body.");
            task.State = ComponentTaskState.Failed;
            task.Completion.TrySetException(exception);
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

        /// <summary><c>canon subtask.cancel async?</c> — request
        /// cancellation of the child task. Transitions the child's
        /// state and faults its completion; the <paramref name="asyncFlag"/>
        /// is the spec's hint that the caller does/doesn't intend
        /// to wait synchronously — Phase 3 implementation treats
        /// both the same (the caller decides whether to await the
        /// canceled completion).</summary>
        public void SubtaskCancel(int subtaskHandle, bool asyncFlag)
        {
            var sub = Subtasks.Get(subtaskHandle)
                ?? throw new InvalidOperationException(
                    $"subtask.cancel: handle {subtaskHandle} not allocated.");
            var child = sub.Child;
            // Idempotent: only transition out of running states.
            if (child.State == ComponentTaskState.Starting
                || child.State == ComponentTaskState.Started)
            {
                child.State = ComponentTaskState.Cancelled;
                child.Completion.TrySetCanceled();
            }
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
            var slot = new StreamSlot { Buffer = new StreamBuffer<byte>(capacity) };
            return Streams.Allocate(slot);
        }

        // Resolve the StreamSlot for a handle, throwing with the
        // canon-op name on failure.
        private StreamSlot GetStreamSlot(int handle, string canonOp)
        {
            var raw = Streams.Get(handle)
                ?? throw new InvalidOperationException(
                    $"{canonOp}: handle {handle} is not allocated.");
            return raw as StreamSlot
                ?? throw new InvalidOperationException(
                    $"{canonOp}: handle {handle} is not a byte stream.");
        }

        /// <summary>Non-blocking write of a byte to the stream.
        /// Returns false when the buffer is at capacity — backpressure.
        /// Slice D adds the suspend-on-full variant.</summary>
        public bool StreamTryWrite(int streamHandle, byte value)
        {
            var slot = GetStreamSlot(streamHandle, "stream.write");
            return slot.Buffer.TryWrite(value);
        }

        /// <summary>Non-blocking read of a byte. Returns false when
        /// the buffer is empty.</summary>
        public bool StreamTryRead(int streamHandle, out byte value)
        {
            value = 0;
            var raw = Streams.Get(streamHandle);
            if (raw is not StreamSlot slot) return false;
            return slot.Buffer.TryRead(out value);
        }

        /// <summary><c>canon stream.write t opts</c> with memory
        /// access — read <paramref name="length"/> bytes from
        /// <paramref name="memory"/> starting at
        /// <paramref name="ptr"/> and write them to the stream.
        /// Returns the number of bytes actually written (less than
        /// <paramref name="length"/> when the buffer fills up).
        /// </summary>
        public int StreamWriteFromMemory(
            int streamHandle, MemoryInstance memory, uint ptr, int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return 0;
            var slot = GetStreamSlot(streamHandle, "stream.write");
            var src = memory.AsSpan((int)ptr, length);
            int written = 0;
            for (int i = 0; i < length; i++)
            {
                if (!slot.Buffer.TryWrite(src[i])) break;
                written++;
            }
            return written;
        }

        /// <summary><c>canon stream.read t opts</c> with memory
        /// access — read up to <paramref name="capacity"/> bytes
        /// from the stream and write them to
        /// <paramref name="memory"/> starting at
        /// <paramref name="ptr"/>. Returns the number of bytes
        /// actually transferred (less than <paramref name="capacity"/>
        /// when the stream had less data, or zero when empty).
        /// </summary>
        public int StreamReadToMemory(
            int streamHandle, MemoryInstance memory, uint ptr, int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (capacity == 0) return 0;
            var slot = GetStreamSlot(streamHandle, "stream.read");
            var dst = memory.AsSpan((int)ptr, capacity);
            int read = 0;
            for (int i = 0; i < capacity; i++)
            {
                if (!slot.Buffer.TryRead(out var b)) break;
                dst[i] = b;
                read++;
            }
            return read;
        }

        /// <summary><c>canon stream.cancel-read t async?</c> —
        /// signals the stream that no further reads will arrive.
        /// The buffer keeps any unread items (writer may still call
        /// <see cref="StreamTryWrite"/>); the reader half is marked
        /// done. Returns true on a known handle.</summary>
        public bool StreamCancelRead(int streamHandle, bool asyncFlag)
        {
            // For the byte stream slice, cancel-read is observationally
            // equivalent to drop-readable: pending and future reads
            // will see end-of-stream. Distinguishing the two arms
            // (sync vs. async hint) is a Slice F refinement.
            return StreamDropReadable(streamHandle);
        }

        /// <summary><c>canon stream.cancel-write t async?</c> —
        /// signals the stream that no further writes will arrive.
        /// Completes the channel so the reader observes EOS.</summary>
        public bool StreamCancelWrite(int streamHandle, bool asyncFlag) =>
            StreamDropWritable(streamHandle);

        /// <summary><c>canon stream.drop-readable t</c> — drop the
        /// reader half of the stream handle. The slot stays in the
        /// table until <see cref="StreamDropWritable"/> also fires;
        /// pending writes effectively go nowhere once the reader
        /// is dropped (the spec allows the runtime to short-circuit
        /// further writes, but this implementation just keeps
        /// buffering until the writer-side also closes).</summary>
        public bool StreamDropReadable(int streamHandle)
        {
            if (Streams.Get(streamHandle) is not StreamSlot slot)
                return false;
            slot.ReaderDropped = true;
            if (slot.WriterDropped) Streams.Drop(streamHandle);
            return true;
        }

        /// <summary><c>canon stream.drop-writable t</c> — drop the
        /// writer half. Completes the channel so the reader observes
        /// end-of-stream once the buffer drains; the table slot is
        /// retained until the reader-side also drops.</summary>
        public bool StreamDropWritable(int streamHandle)
        {
            if (Streams.Get(streamHandle) is not StreamSlot slot)
                return false;
            slot.Buffer.Complete();
            slot.WriterDropped = true;
            if (slot.ReaderDropped) Streams.Drop(streamHandle);
            return true;
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

        /// <summary><c>canon future.cancel-read t async?</c> —
        /// abandon the reader side. Pending read is cancelled.</summary>
        public bool FutureCancelRead(int futureHandle, bool asyncFlag) =>
            FutureDropReadable(futureHandle);

        /// <summary><c>canon future.cancel-write t async?</c> —
        /// abandon the writer side without resolving. Reader
        /// observes cancellation.</summary>
        public bool FutureCancelWrite(int futureHandle, bool asyncFlag) =>
            FutureDropReadable(futureHandle);

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

        /// <summary>Memory-aware variant of
        /// <see cref="ErrorContextNew(string)"/>: reads the debug
        /// message as a UTF-8 string from
        /// <paramref name="memory"/> at <paramref name="ptr"/>,
        /// <paramref name="len"/>, then allocates a handle.</summary>
        public int ErrorContextNewFromMemory(
            MemoryInstance memory, uint ptr, uint len)
        {
            var bytes = memory.AsSpan((int)ptr, (int)len);
            var str = Encoding.UTF8.GetString(bytes);
            return ErrorContextNew(str);
        }

        /// <summary>Memory-aware variant of
        /// <see cref="ErrorContextDebugMessage(int)"/>: writes the
        /// allocated string into <paramref name="memory"/> at
        /// <paramref name="dstPtr"/> as UTF-8. The caller is
        /// responsible for ensuring <paramref name="dstPtr"/>
        /// addresses a sufficiently-sized buffer; spec-compliant
        /// callers use <c>cabi_realloc</c> to allocate first via
        /// the returned byte count.
        ///
        /// <para>Returns the number of UTF-8 bytes written. Returns
        /// the message's required byte count even when
        /// <paramref name="dstPtr"/> is 0 — that's the spec-defined
        /// way to query the size before allocating.</para>
        /// </summary>
        public int ErrorContextDebugMessageToMemory(
            int errorContextHandle, MemoryInstance memory, uint dstPtr)
        {
            var msg = ErrorContexts.Get(errorContextHandle)
                ?? throw new InvalidOperationException(
                    $"error-context.debug-message: handle {errorContextHandle} not allocated.");
            int byteCount = Encoding.UTF8.GetByteCount(msg);
            if (dstPtr != 0)
            {
                memory.WriteUtf8String(dstPtr, msg, nullTerminate: false);
            }
            return byteCount;
        }

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
        /// state. Needs <see cref="IContinuationContext"/>-driven
        /// suspend integration with the interpreter loop; lands in
        /// Slice F together with the end-to-end producer/consumer
        /// fixture.</summary>
        public void WaitableSetWait(
            IContinuationContext ctx, int waitableSetHandle,
            int memoryIdx, bool cancellable)
        {
            throw new NotImplementedException(
                "waitable-set.wait suspend integration lands in Slice F.");
        }

        /// <summary><c>canon waitable-set.poll cancel? memidx</c> —
        /// non-blocking check. Returns the handle of the first
        /// deliverable member, or 0 (the canon null sentinel) when
        /// no member is ready. Deliverable = the underlying
        /// <see cref="ComponentTask"/> is past Started,
        /// <see cref="FutureCell{T}"/> is completed, or
        /// <see cref="StreamBuffer{T}"/> has buffered items or has
        /// completed.</summary>
        public int WaitableSetPoll(
            IContinuationContext ctx, int waitableSetHandle,
            int memoryIdx, bool cancellable)
        {
            var ws = WaitableSets.Get(waitableSetHandle)
                ?? throw new InvalidOperationException(
                    $"waitable-set.poll: handle {waitableSetHandle} not allocated.");
            foreach (var memberHandle in ws.Members)
            {
                if (IsWaitableDeliverable(memberHandle)) return memberHandle;
            }
            return 0; // canon null
        }

        // Determine whether a waitable handle has reached a state
        // that wait/poll should surface. Handles are not partitioned
        // by kind on the wire — same int can appear in any of the
        // tables — so check each kind in turn.
        private bool IsWaitableDeliverable(int waitableHandle)
        {
            if (Tasks.Get(waitableHandle) is ComponentTask t)
                return t.Completion.Task.IsCompleted;
            if (Subtasks.Get(waitableHandle) is ComponentSubtask st)
                return st.Child.Completion.Task.IsCompleted;
            if (Futures.Get(waitableHandle) is FutureCell<object?> f)
                return f.IsCompleted;
            if (Streams.Get(waitableHandle) is StreamSlot ss)
                return ss.Buffer.IsCompleted || ss.Buffer.Reader.Count > 0;
            return false;
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

        // ---- Backpressure -----------------------------------------------

        /// <summary><c>canon backpressure.set</c> — clear the
        /// component's backpressure flag. Embedders are free to
        /// resume creating new tasks.</summary>
        public void BackpressureSet() { _backpressureLevel = 0; }

        /// <summary><c>canon backpressure.inc</c> — raise the
        /// backpressure level by one. Multiple increments stack;
        /// the embedder reads <see cref="BackpressureLevel"/>.</summary>
        public void BackpressureInc() { _backpressureLevel++; }

        /// <summary><c>canon backpressure.dec</c> — drop the
        /// level by one (floor 0).</summary>
        public void BackpressureDec()
        {
            if (_backpressureLevel > 0) _backpressureLevel--;
        }

        // ---- Context ----------------------------------------------------

        /// <summary><c>canon context.get v i</c> — read the ambient
        /// task's context slot <paramref name="slotIdx"/>. Returns
        /// a default-initialized <see cref="Value"/> when the slot
        /// has never been written. Throws when no task is ambient.</summary>
        public Value ContextGet(int slotIdx)
        {
            var task = CurrentTask
                ?? throw new InvalidOperationException(
                    "context.get called outside an active task body.");
            return task.Context.TryGetValue(slotIdx, out var v) ? v : default;
        }

        /// <summary><c>canon context.set v i</c> — write the ambient
        /// task's context slot. Throws when no task is ambient.</summary>
        public void ContextSet(int slotIdx, Value value)
        {
            var task = CurrentTask
                ?? throw new InvalidOperationException(
                    "context.set called outside an active task body.");
            task.Context[slotIdx] = value;
        }

        // ---- Thread.yield -----------------------------------------------

        /// <summary><c>canon thread.yield cancel?</c> — yield the
        /// current task's slot to other runnable tasks. In a
        /// single-task body (the only model Phase 3 implements),
        /// there are no other tasks to schedule, so yield is a
        /// synchronous no-op. <paramref name="cancellable"/>
        /// controls whether task-cancellation is observable across
        /// the yield boundary; today both arms are identical
        /// because there is no scheduler that could interleave.
        ///
        /// <para>When Phase 3 grows a multi-task scheduler, this
        /// method becomes the runtime's natural cooperative
        /// yield point — at that time the suspend integration
        /// with <see cref="IContinuationContext"/> lands.</para>
        /// </summary>
        public void ThreadYield(IContinuationContext ctx, bool cancellable)
        {
            // Intentional no-op. See doc comment.
        }
    }
}
