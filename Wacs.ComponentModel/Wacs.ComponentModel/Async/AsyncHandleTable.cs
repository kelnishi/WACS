// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Per-component handle table for the Component Model async ABI:
    /// allocates handles for <c>task</c>, <c>subtask</c>,
    /// <c>waitable-set</c>, <c>stream</c>, and <c>future</c> values.
    /// Each instance of this generic backs one handle space — one
    /// per <c>(component, handle-kind)</c> tuple, mirroring how
    /// the existing <see cref="Wacs.ComponentModel.Harness.Runtime.HostHandleTable"/>
    /// backs per-resource-type spaces.
    ///
    /// <para>Wire convention: handles are 4-byte positive integers.
    /// Handle <c>0</c> is reserved as the null/no-handle sentinel —
    /// the table never allocates it. Freelist recycling reuses
    /// dropped handles so long-running components don't drift their
    /// counter unbounded.</para>
    ///
    /// <para><b>Thread-safety:</b> not thread-safe by design. The
    /// table assumes one wasm execution context calls into it at a
    /// time — true for:</para>
    /// <list type="bullet">
    ///   <item>Core CM async (the stackful proposal Phase 3 targets):
    ///     guest yields via Stack Switching <c>suspend</c>/<c>resume</c>,
    ///     not CLR thread pool.</item>
    ///   <item>The non-shared thread.* canon ops (bytes 0x26–0x2b,
    ///     🧵 in the spec): cooperatively-scheduled fibers on a
    ///     single OS thread per instance.</item>
    /// </list>
    /// <para>Host callbacks (e.g. completing a host-side
    /// <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>
    /// from a thread-pool thread) resolve through the dispatcher,
    /// which marshals back to the wasm execution context — they
    /// never call into this table directly.</para>
    /// <para>Would require concurrent storage if the
    /// explicit-threads proposal (🧵② shared variants, bytes
    /// 0x40–0x42) ever lands — that case is rejected at the
    /// parser today.</para>
    /// </summary>
    public sealed class AsyncHandleTable<T> where T : class
    {
        private readonly Dictionary<int, T> _entries = new Dictionary<int, T>();
        private readonly Stack<int> _freelist = new Stack<int>();
        private int _nextHandle = 1; // 0 is reserved

        /// <summary>Allocate a fresh slot. Reuses a dropped handle when one is free.</summary>
        public int Allocate(T value)
        {
            int handle = _freelist.Count > 0 ? _freelist.Pop() : _nextHandle++;
            _entries[handle] = value;
            return handle;
        }

        /// <summary>
        /// Allocate a slot whose value depends on its own handle.
        /// The <paramref name="factory"/> is invoked with the
        /// freshly-minted handle and returns the value to bind to
        /// it. Used when the stored value (e.g.
        /// <see cref="ComponentTask"/>, <see cref="ComponentWaitableSet"/>)
        /// carries its handle as a field — avoids the
        /// allocate-then-rewrite dance.
        /// </summary>
        public int Allocate(Func<int, T> factory)
        {
            int handle = _freelist.Count > 0 ? _freelist.Pop() : _nextHandle++;
            _entries[handle] = factory(handle);
            return handle;
        }

        /// <summary>Lookup; returns null when the handle was never allocated or is dropped.</summary>
        public T? Get(int handle) =>
            _entries.TryGetValue(handle, out var v) ? v : null;

        /// <summary>
        /// Release the slot back to the table. Returns the dropped
        /// value (so callers can run a release callback on it) or
        /// null when the handle was already absent.
        /// </summary>
        public T? Drop(int handle)
        {
            if (!_entries.TryGetValue(handle, out var v)) return null;
            _entries.Remove(handle);
            _freelist.Push(handle);
            return v;
        }

        /// <summary>True iff <paramref name="handle"/> currently maps to a live value.</summary>
        public bool Contains(int handle) => _entries.ContainsKey(handle);

        /// <summary>Live entry count.</summary>
        public int Count => _entries.Count;
    }
}
