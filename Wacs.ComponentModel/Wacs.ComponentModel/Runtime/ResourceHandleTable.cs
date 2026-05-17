// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Per-component-instance handle table for one exported
    /// resource type. Backs the canonical-ABI
    /// <c>canon resource.new / resource.drop / resource.rep</c>
    /// intrinsics — host code in <see cref="ComponentInstance"/>
    /// constructs one of these per <c>(component, resourceTypeIdx)</c>
    /// and binds three adapter functions over it as core-module
    /// imports the inner wasm expects.
    ///
    /// <para>v1 doesn't distinguish own vs borrow handles — the
    /// table is a simple <c>int handle → int rep</c> map. A
    /// borrow-aware variant is a future tightening (own can be
    /// dropped; borrow must not be dropped past its call scope).</para>
    /// </summary>
    internal sealed class ResourceHandleTable
    {
        // 0 is reserved as the "invalid" sentinel — the harness
        // class's Dispose() zeros _handle to that value to make
        // re-disposal a no-op.
        private const int InvalidHandle = 0;

        private readonly Dictionary<int, int> _slots = new();
        private readonly Stack<int> _freelist = new();
        private int _nextHandle = 1;

        /// <summary>
        /// Allocate a fresh handle for <paramref name="rep"/>.
        /// Reuses freelist entries before growing
        /// <see cref="_nextHandle"/> so handle values stay
        /// small even with churn.
        /// </summary>
        public int New(int rep)
        {
            int h = _freelist.Count > 0 ? _freelist.Pop() : _nextHandle++;
            _slots[h] = rep;
            return h;
        }

        /// <summary>
        /// Look up the rep for <paramref name="handle"/>. Throws
        /// on unknown handles — wasm-side callers should always
        /// have a live handle when they reach this op (the
        /// canonical ABI guarantees lift produces only valid
        /// handles, and the host has to drop them explicitly).
        /// </summary>
        public int Rep(int handle)
        {
            if (!_slots.TryGetValue(handle, out var rep))
                throw new InvalidOperationException(
                    $"Resource handle {handle} not in table (already dropped or never allocated).");
            return rep;
        }

        /// <summary>
        /// Drop <paramref name="handle"/>, returning the rep so
        /// the caller can pass it to the resource's dtor. The
        /// slot is added to the freelist for reuse.
        /// </summary>
        public int Drop(int handle)
        {
            if (handle == InvalidHandle)
                throw new InvalidOperationException(
                    "Cannot drop invalid handle 0.");
            if (!_slots.Remove(handle, out var rep))
                throw new InvalidOperationException(
                    $"Resource handle {handle} not in table (double-drop or never allocated?).");
            _freelist.Push(handle);
            return rep;
        }

        /// <summary>True iff <paramref name="handle"/> is currently allocated.</summary>
        public bool Contains(int handle) => _slots.ContainsKey(handle);

        /// <summary>Live handle count — exposed for tests.</summary>
        public int LiveCount => _slots.Count;
    }
}
