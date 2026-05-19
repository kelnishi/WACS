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

        /// <summary>The component's exported memory, set by the
        /// host after core-module instantiation. Memory-touching
        /// canon ops (stream.read/write, error-context.new) +
        /// the canon-async lift adapter (string/list/option/result
        /// results for task.return) read from here. Null when the
        /// host hasn't yet wired the memory, in which case
        /// memory-touching ops throw.</summary>
        public Wacs.Core.Runtime.Types.MemoryInstance? Memory { get; set; }

        /// <summary>The string-encoding option resolved from the
        /// canon-lift's opts during component instantiation
        /// (<c>(string-encoding utf8|utf16|latin1+utf16)</c>).
        /// The lift adapter consults this to pick the right
        /// <see cref="StringMarshal"/> variant for string-typed
        /// canon-async results. Default UTF-8.</summary>
        public Wacs.ComponentModel.Runtime.Parser.CanonOption.Kind StringEncoding { get; set; }
            = Wacs.ComponentModel.Runtime.Parser.CanonOption.Kind.StringUtf8;

        /// <summary>The component's type table, set by the host
        /// at dispatcher allocation time. The lift adapter
        /// resolves typeidx references in canon entries (e.g.
        /// <c>task.return list&lt;u8&gt;</c>) by indexing into
        /// this list to discover the
        /// <see cref="Wacs.ComponentModel.Runtime.Parser.DefTypeEntry"/>
        /// shape. Null when the host hasn't wired it — aggregate
        /// lift paths return null delegates in that case.</summary>
        public IReadOnlyList<Wacs.ComponentModel.Runtime.Parser.DefTypeEntry>? Types
        { get; set; }

        // ---- Typed-lifter mapping --------------------------------------
        //
        // Bindgen-emitted code calls RegisterTypeMapping at
        // instantiation time to declare "type-table index N is
        // WIT identifier X". The canon-async binder, when
        // building a `task.return` delegate for an aggregate at
        // type-table index N, queries this map for a typed lifter
        // before falling back to the per-arity helpers in
        // CanonAsyncBinder.
        //
        // The map is sparse — only indices the bindgen knows
        // about are populated. Per-call lookup is allocation-free
        // (Dictionary lookup, no boxing of typeIdx).

        private readonly Dictionary<uint, string> _typeIdxToWitIdent =
            new Dictionary<uint, string>();

        /// <summary>
        /// Declare that type-table index <paramref name="typeIdx"/>
        /// corresponds to the WIT-level identifier
        /// <paramref name="witIdent"/>. Idempotent — re-registering
        /// the same (idx, ident) pair is a no-op. Re-registering a
        /// different ident for the same index throws to surface a
        /// bindgen bug rather than silently overwriting.
        /// </summary>
        public void RegisterTypeMapping(uint typeIdx, string witIdent)
        {
            if (witIdent == null) throw new ArgumentNullException(nameof(witIdent));
            if (_typeIdxToWitIdent.TryGetValue(typeIdx, out var existing))
            {
                if (!string.Equals(existing, witIdent, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Type-table index {typeIdx} already mapped to " +
                        $"WIT identifier '{existing}'; refusing to overwrite " +
                        $"with '{witIdent}'.");
                return;
            }
            _typeIdxToWitIdent[typeIdx] = witIdent;
        }

        /// <summary>
        /// Try to resolve a typed lifter for the value at type-table
        /// index <paramref name="typeIdx"/>. Returns true and a
        /// non-null <paramref name="lifter"/> iff the index has
        /// been registered via
        /// <see cref="RegisterTypeMapping(uint, string)"/> AND a
        /// matching <c>[ComponentLifter]</c>-decorated method exists
        /// in <see cref="ComponentLifterRegistry"/>. Otherwise the
        /// canon-async binder falls back to its per-arity helpers.
        /// </summary>
        public bool TryGetTypedLifterForTypeIdx(
            uint typeIdx, out Delegate? lifter)
        {
            if (!_typeIdxToWitIdent.TryGetValue(typeIdx, out var ident))
            {
                lifter = null;
                return false;
            }
            return ComponentLifterRegistry.TryGet(ident, out lifter);
        }

        // ---- Current-task stack ----------------------------------------
        //
        // The "ambient task" is the task whose body is currently
        // executing on the wasm stack. The lift adapter (Slice E)
        // pushes a fresh task on body entry and pops on
        // return/cancel/exception. Canon ops that reference the
        // current task (task.return, task.cancel, context.get/set)
        // peek the top.

        // Flow-context-aware task frame: each push allocates a new
        // immutable frame node and stores it as the AsyncLocal
        // value, so sibling async bodies (each with its own
        // ExecutionContext) see their OWN task stack. Without this,
        // two cooperatively-yielding bodies on the same dispatcher
        // clobber each other's CurrentTask state.
        private sealed class TaskFrame
        {
            public readonly ComponentTask Task;
            public readonly TaskFrame? Parent;
            public TaskFrame(ComponentTask task, TaskFrame? parent)
            {
                Task = task;
                Parent = parent;
            }
        }

        private readonly System.Threading.AsyncLocal<TaskFrame?> _currentTaskFrame =
            new System.Threading.AsyncLocal<TaskFrame?>();

        /// <summary>The task whose body is currently executing on
        /// the calling ExecutionContext, or <c>null</c> when none.
        /// Flow-aware: distinct async bodies on the same dispatcher
        /// see distinct ambient tasks.</summary>
        public ComponentTask? CurrentTask => _currentTaskFrame.Value?.Task;

        /// <summary>Lift-adapter entry point: push a freshly-created
        /// task as the ambient task in the current ExecutionContext.
        /// Transitions <see cref="ComponentTaskState.Starting"/> →
        /// <see cref="ComponentTaskState.Started"/>.</summary>
        public void PushCurrentTask(ComponentTask task)
        {
            task.State = ComponentTaskState.Started;
            _currentTaskFrame.Value = new TaskFrame(task, _currentTaskFrame.Value);
        }

        /// <summary>Lift-adapter exit point: pop the ambient task
        /// from the current ExecutionContext's frame chain. Returns
        /// the popped task or null if the chain was empty.</summary>
        public ComponentTask? PopCurrentTask()
        {
            var frame = _currentTaskFrame.Value;
            if (frame == null) return null;
            _currentTaskFrame.Value = frame.Parent;
            return frame.Task;
        }

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
        [CanonAsync]
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
        [CanonAsync]
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
        [CanonAsync]
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
        [CanonAsync]
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
        [CanonAsync]
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

        /// <summary>
        /// Expose the underlying <see cref="StreamBuffer{T}"/>
        /// for a byte stream handle, or null when the handle is
        /// absent / wrong-typed. Host bridges (e.g. the
        /// <c>StreamBackedSink</c> in WACS.WASI.Preview3) use this
        /// to subscribe to the buffer's
        /// <see cref="System.Threading.Channels.ChannelReader{T}.WaitToReadAsync"/>
        /// signal instead of polling.
        /// </summary>
        public StreamBuffer<byte>? GetByteStreamBuffer(int streamHandle)
        {
            return (Streams.Get(streamHandle) as StreamSlot)?.Buffer;
        }

        /// <summary><c>canon stream.write t opts</c> with memory
        /// access — read <paramref name="length"/> bytes from
        /// <paramref name="memory"/> starting at
        /// <paramref name="ptr"/> and write them to the stream.
        /// Returns the number of bytes actually written (less than
        /// <paramref name="length"/> when the buffer fills up).
        /// </summary>
        [CanonAsync("stream-write")]
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
        [CanonAsync("stream-read")]
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
        [CanonAsync]
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
        [CanonAsync]
        public bool StreamCancelWrite(int streamHandle, bool asyncFlag) =>
            StreamDropWritable(streamHandle);

        /// <summary><c>canon stream.drop-readable t</c> — drop the
        /// reader half of the stream handle. The slot stays in the
        /// table until <see cref="StreamDropWritable"/> also fires;
        /// pending writes effectively go nowhere once the reader
        /// is dropped (the spec allows the runtime to short-circuit
        /// further writes, but this implementation just keeps
        /// buffering until the writer-side also closes).</summary>
        [CanonAsync]
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
        [CanonAsync]
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
        [CanonAsync]
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
        [CanonAsync]
        public bool FutureCancelRead(int futureHandle, bool asyncFlag) =>
            FutureDropReadable(futureHandle);

        /// <summary><c>canon future.cancel-write t async?</c> —
        /// abandon the writer side without resolving. Reader
        /// observes cancellation.</summary>
        [CanonAsync]
        public bool FutureCancelWrite(int futureHandle, bool asyncFlag) =>
            FutureDropReadable(futureHandle);

        /// <summary><c>canon future.drop-readable t</c>. Cancels any
        /// pending reader and drops the handle.</summary>
        [CanonAsync]
        public bool FutureDropReadable(int futureHandle)
        {
            if (Futures.Get(futureHandle) is FutureCell<object?> cell)
                cell.TrySetCanceled();
            return Futures.Drop(futureHandle) != null;
        }

        /// <summary><c>canon future.drop-writable t</c>. Same as
        /// drop-readable at this slice; full single-direction
        /// semantics land in Slice D.</summary>
        [CanonAsync]
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
        [CanonAsync("error-context-new")]
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
        [CanonAsync("error-context-debug-message")]
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
        [CanonAsync]
        public bool ErrorContextDrop(int errorContextHandle) =>
            ErrorContexts.Drop(errorContextHandle) != null;

        // ---- Waitable set ------------------------------------------------

        /// <summary><c>canon waitable-set.new</c>.</summary>
        [CanonAsync]
        public int WaitableSetNew() =>
            WaitableSets.Allocate(handle => new ComponentWaitableSet(handle));

        /// <summary><c>canon waitable-set.wait cancel? memidx</c> —
        /// block until any member of the set reaches a deliverable
        /// state. Returns the handle of the first deliverable
        /// member (any member already deliverable when called wins).
        ///
        /// <para><b>Two flavors share the same body:</b>
        /// this synchronous entry point is the canon-async binder's
        /// default, intended for wasm bodies that run synchronously
        /// on the calling CLR thread.
        /// <see cref="WaitableSetWaitAsync(IContinuationContext, int, int, bool, System.Threading.CancellationToken)"/>
        /// is the cooperative-yield variant for async wasm bodies
        /// (the lift adapter's
        /// <see cref="AsyncLiftAdapter.InvokeAsync(AsyncDispatcher, ContInstance, System.Func{Task})"/>
        /// overload) — same wait semantics, but the CLR thread
        /// is free to run other work while parked.</para>
        ///
        /// <para>The wasm-side stackful suspend (where the wasm
        /// continuation captures via <c>cont.new</c> /
        /// <c>suspend</c> and yields control back to a wasm-defined
        /// dispatch loop) is a future refinement that lights up
        /// when wit-component-emitted async components actually
        /// use the Stack Switching opcodes. The current async-body
        /// path covers the CLR-side concurrency case adequately.</para>
        ///
        /// <para>Returns <c>0</c> (canon null) when the set is
        /// empty. Members without a wait-able completion source
        /// (e.g. raw stream handles with no buffered data and no
        /// EOS yet) are still polled on each wakeup, so a stream
        /// becoming ready re-arms the loop.</para>
        ///
        /// <para>The <paramref name="memoryIdx"/> parameter is the
        /// spec-defined target where canon-spec implementations
        /// write the deliverable-event struct. This implementation
        /// returns the handle directly via the method's return
        /// value instead — the canon-ABI memory write lands when
        /// the lift adapter generates per-call memory marshaling.</para>
        /// </summary>
        [CanonAsync]
        public int WaitableSetWait(
            IContinuationContext ctx, int waitableSetHandle,
            int memoryIdx, bool cancellable)
        {
            // Sync entry: block on the async body. GetAwaiter().GetResult()
            // unwraps the Task<int> and rethrows any inner exception
            // without the AggregateException wrapper that .Result
            // would introduce.
            return WaitableSetWaitAsync(
                ctx, waitableSetHandle, memoryIdx,
                cancellable, default).GetAwaiter().GetResult();
        }

        /// <summary>Cooperative-yield variant of
        /// <see cref="WaitableSetWait(IContinuationContext, int, int, bool)"/>.
        /// Returns a <see cref="Task{T}"/> that completes when any
        /// member of the set reaches a deliverable state — the
        /// CLR thread is yielded to the host scheduler while
        /// parked, so other tasks on the same scheduler can run.
        ///
        /// <para>Use this from async wasm bodies launched via
        /// <see cref="AsyncLiftAdapter.InvokeAsync(AsyncDispatcher, ContInstance, System.Func{Task})"/>.</para>
        ///
        /// <para>Honors
        /// <paramref name="cancellationToken"/>: cancellation
        /// surfaces as <see cref="OperationCanceledException"/>
        /// from the awaiting body. The <paramref name="cancellable"/>
        /// flag is the wasm-side hint that the runtime is free to
        /// cancel-on-deliver if needed — implementation does not
        /// currently distinguish, both flavors honor the token.</para>
        /// </summary>
        public async Task<int> WaitableSetWaitAsync(
            IContinuationContext ctx, int waitableSetHandle,
            int memoryIdx, bool cancellable,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var ws = WaitableSets.Get(waitableSetHandle)
                ?? throw new InvalidOperationException(
                    $"waitable-set.wait: handle {waitableSetHandle} not allocated.");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Sync scan — wins immediately when any member is
                // already deliverable, so a polling caller never
                // pays the WhenAny overhead.
                foreach (var member in ws.Members)
                    if (IsWaitableDeliverable(member)) return member;

                if (ws.Members.Count == 0) return 0;

                // Build the WhenAny-able task set. Streams use the
                // channel reader's WaitToReadAsync to wake on
                // buffered-data or EOS. Tasks/subtasks/futures use
                // their completion task directly.
                var waitables = new List<Task>(ws.Members.Count);
                foreach (var member in ws.Members)
                {
                    var t = GetMemberWaitTask(member);
                    if (t != null) waitables.Add(t);
                }

                if (waitables.Count == 0)
                {
                    // No member has a wait-able source — only
                    // happens when every member is a stream
                    // without a producer-side TCS. Treat as
                    // empty-set wakeup.
                    return 0;
                }

                // WhenAny + cancellation: race the wait against a
                // cancellation-triggered TCS so cancellationToken
                // cancellation unblocks promptly without abandoning
                // the underlying tasks.
                if (cancellationToken.CanBeCanceled)
                {
                    var cancelTcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancellationToken.Register(
                        s => ((TaskCompletionSource<bool>)s!).TrySetResult(true),
                        cancelTcs))
                    {
                        waitables.Add(cancelTcs.Task);
                        await Task.WhenAny(waitables).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    await Task.WhenAny(waitables).ConfigureAwait(false);
                }
                // Loop back and re-scan: WhenAny only proves SOME
                // task completed, not which one.
            }
        }

        // Build a Task that completes when the given waitable
        // handle becomes deliverable. Returns null for handles
        // we can't wait on (unknown / non-waitable shapes).
        //
        // <para><b>Handle-space caveat:</b> each AsyncHandleTable
        // assigns its own monotonic <c>_nextHandle</c>, so the
        // same integer can refer to entries across multiple
        // tables (e.g. registered Task at handle 1 AND fresh
        // future at handle 1). We build a Task that fires when
        // ANY of the entries the handle resolves to becomes
        // ready — sympathetic to the CM spec's single-namespace
        // model — to avoid short-circuiting on the
        // wrong table.</para>
        private Task? GetMemberWaitTask(int handle)
        {
            List<Task>? tasks = null;
            if (Tasks.Get(handle) is ComponentTask t)
                (tasks ??= new List<Task>()).Add(t.Completion.Task);
            if (Subtasks.Get(handle) is ComponentSubtask st)
                (tasks ??= new List<Task>()).Add(st.Child.Completion.Task);
            if (Futures.Get(handle) is FutureCell<object?> f)
                (tasks ??= new List<Task>()).Add(f.Task);
            if (Streams.Get(handle) is StreamSlot ss)
            {
                var vt = ss.Buffer.Reader.WaitToReadAsync();
                (tasks ??= new List<Task>()).Add(
                    vt.IsCompleted ? Task.CompletedTask : vt.AsTask());
            }
            if (tasks == null) return null;
            if (tasks.Count == 1) return tasks[0];
            return Task.WhenAny(tasks);
        }

        /// <summary><c>canon waitable-set.poll cancel? memidx</c> —
        /// non-blocking check. Returns the handle of the first
        /// deliverable member, or 0 (the canon null sentinel) when
        /// no member is ready. Deliverable = the underlying
        /// <see cref="ComponentTask"/> is past Started,
        /// <see cref="FutureCell{T}"/> is completed, or
        /// <see cref="StreamBuffer{T}"/> has buffered items or has
        /// completed.</summary>
        [CanonAsync]
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
        // that wait/poll should surface. The handle can resolve
        // to entries across multiple tables (the per-kind
        // <see cref="AsyncHandleTable{T}"/> instances each allocate
        // their own monotone <c>_nextHandle</c>); any one of the
        // resolved entries reaching a deliverable state makes the
        // handle deliverable — sympathetic to the CM spec's
        // single-namespace model.
        private bool IsWaitableDeliverable(int waitableHandle)
        {
            if (Tasks.Get(waitableHandle) is ComponentTask t
                && t.Completion.Task.IsCompleted) return true;
            if (Subtasks.Get(waitableHandle) is ComponentSubtask st
                && st.Child.Completion.Task.IsCompleted) return true;
            if (Futures.Get(waitableHandle) is FutureCell<object?> f
                && f.IsCompleted) return true;
            if (Streams.Get(waitableHandle) is StreamSlot ss
                && (ss.Buffer.IsCompleted || ss.Buffer.Reader.Count > 0))
                return true;
            return false;
        }

        /// <summary><c>canon waitable-set.drop</c>.</summary>
        [CanonAsync]
        public bool WaitableSetDrop(int waitableSetHandle) =>
            WaitableSets.Drop(waitableSetHandle) != null;

        /// <summary><c>canon waitable.join</c> — adds the current
        /// waitable to the set. The "current waitable-set context"
        /// is needed; Slice D wires it.</summary>
        [CanonAsync]
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
        [CanonAsync]
        public void BackpressureSet() { _backpressureLevel = 0; }

        /// <summary><c>canon backpressure.inc</c> — raise the
        /// backpressure level by one. Multiple increments stack;
        /// the embedder reads <see cref="BackpressureLevel"/>.</summary>
        [CanonAsync]
        public void BackpressureInc() { _backpressureLevel++; }

        /// <summary><c>canon backpressure.dec</c> — drop the
        /// level by one (floor 0).</summary>
        [CanonAsync]
        public void BackpressureDec()
        {
            if (_backpressureLevel > 0) _backpressureLevel--;
        }

        // ---- Context ----------------------------------------------------

        /// <summary><c>canon context.get v i</c> — read the ambient
        /// task's context slot <paramref name="slotIdx"/>. Returns
        /// a default-initialized <see cref="Value"/> when the slot
        /// has never been written. Throws when no task is ambient.</summary>
        [CanonAsync]
        public Value ContextGet(int slotIdx)
        {
            var task = CurrentTask
                ?? throw new InvalidOperationException(
                    "context.get called outside an active task body.");
            return task.Context.TryGetValue(slotIdx, out var v) ? v : default;
        }

        /// <summary><c>canon context.set v i</c> — write the ambient
        /// task's context slot. Throws when no task is ambient.</summary>
        [CanonAsync]
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
        [CanonAsync]
        public void ThreadYield(IContinuationContext ctx, bool cancellable)
        {
            // Intentional no-op. See doc comment.
        }
    }
}
