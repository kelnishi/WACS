// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Harness.Runtime
{
    /// <summary>
    /// Per-harness, per-resource-type host-side handle table.
    /// Decoupled from the WASM-side
    /// <see cref="Wacs.Core.Runtime.ResourceHandleTable"/>: the
    /// runtime table keys handles 1:1 with their reps (rep-as-handle,
    /// required for wit-bindgen 0.41 Rust guest compatibility);
    /// this table sits one layer above and allocates an independent
    /// auto-increment handle space the C# user code sees on
    /// <c>Resource.Handle</c>.
    ///
    /// <para>The separation lets the harness track ownership and
    /// lifetime on the host side without depending on the wasm
    /// runtime reusing rep values predictably — a fresh
    /// <c>NewOwn(rep)</c> always yields a fresh host handle even
    /// when the guest reuses the same rep pointer.</para>
    ///
    /// <para>v2 scope: own-only tracking. Borrows go through
    /// <c>Borrowed&lt;T&gt;</c> and wrap the rep directly with no
    /// table entry — call-scope invalidation is deferred (it needs
    /// hooks the runtime doesn't expose yet).</para>
    /// </summary>
    public sealed class HostHandleTable
    {
        // 0 is reserved as the invalid-handle sentinel — Resource
        // sets _hostHandle to 0 after Dispose so re-disposal becomes
        // a no-op without the table even being consulted.
        private const int InvalidHandle = 0;

        // handle → rep mapping for currently-live OWN handles.
        private readonly Dictionary<int, int> _ownedReps = new();

        // Freed host handles to recycle on the next NewOwn — keeps
        // the handle space dense even under high allocation churn.
        // LIFO is fine; consumers shouldn't care about order.
        private readonly Stack<int> _freelist = new();

        // Next-never-issued handle. Skips 0 (reserved sentinel).
        private int _nextHandle = 1;

        /// <summary>
        /// Allocate a fresh host handle wrapping the given wasm-side
        /// <paramref name="rep"/>. Returns a recycled handle from
        /// the freelist when available, otherwise a fresh
        /// auto-incremented value (skipping the 0 sentinel).
        /// </summary>
        public int NewOwn(int rep)
        {
            int handle = _freelist.Count > 0 ? _freelist.Pop() : _nextHandle++;
            _ownedReps[handle] = rep;
            return handle;
        }

        /// <summary>
        /// Look up the wasm-side rep for a live host handle. Throws
        /// when the handle has been dropped or was never allocated.
        /// </summary>
        public int Rep(int handle)
        {
            if (!_ownedReps.TryGetValue(handle, out var rep))
                throw new InvalidOperationException(
                    $"Host handle {handle} not live (already dropped or never allocated).");
            return rep;
        }

        /// <summary>
        /// Drop a live host handle, returning the rep so the caller
        /// can pass it to the resource's dtor / canon-drop. Recycles
        /// the handle slot back to the freelist.
        /// </summary>
        public int DropOwn(int handle)
        {
            if (handle == InvalidHandle)
                throw new InvalidOperationException(
                    "Cannot drop invalid handle 0.");
            if (!_ownedReps.TryGetValue(handle, out var rep))
                throw new InvalidOperationException(
                    $"Host handle {handle} not in table (double-drop or never allocated?).");
            _ownedReps.Remove(handle);
            _freelist.Push(handle);
            return rep;
        }

        /// <summary>True iff <paramref name="handle"/> is currently live.</summary>
        public bool Contains(int handle) => _ownedReps.ContainsKey(handle);

        /// <summary>Live handle count — exposed for tests.</summary>
        public int LiveCount => _ownedReps.Count;
    }
}
