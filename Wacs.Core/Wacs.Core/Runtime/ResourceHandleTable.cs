// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Per-component-instance handle table for one exported
    /// resource type. Backs the canonical-ABI
    /// <c>canon resource.new / resource.drop / resource.rep</c>
    /// intrinsics — <see cref="CanonResourceBinder"/> constructs
    /// one of these per resource and binds three adapter functions
    /// over it as core-module imports the inner wasm expects.
    ///
    /// <para>v1 doesn't distinguish own vs borrow handles — the
    /// table is a simple <c>int handle → int rep</c> map. A
    /// borrow-aware variant is a future tightening (own can be
    /// dropped; borrow must not be dropped past its call scope).</para>
    /// </summary>
    public sealed class ResourceHandleTable
    {
        // 0 is reserved as the "invalid" sentinel — the harness
        // class's Dispose() zeros _handle to that value to make
        // re-disposal a no-op.
        private const int InvalidHandle = 0;

        // v1 uses rep-as-handle 1:1: every operation just tracks
        // which reps are live. wit-bindgen-compiled guests don't
        // import [resource-rep] when emitting their instance-method
        // wrappers — they stash (handle == rep) in static state
        // and dereference it directly. A v2 that supports separate
        // handle/rep spaces would key independently; for now this
        // keeps the binder compatible with the standard toolchain.
        private readonly HashSet<int> _liveReps = new();

        /// <summary>
        /// Record <paramref name="rep"/> as a live handle. Returns
        /// <paramref name="rep"/> itself — v1 is rep-as-handle.
        /// </summary>
        public int New(int rep)
        {
            if (rep == InvalidHandle)
                throw new InvalidOperationException(
                    "Cannot allocate handle for rep 0 (collides with invalid-handle sentinel).");
            if (!_liveReps.Add(rep))
                throw new InvalidOperationException(
                    $"Rep {rep} already registered — duplicate resource.new.");
            return rep;
        }

        /// <summary>
        /// Look up the rep for <paramref name="handle"/>. v1
        /// returns the handle (which IS the rep).
        /// </summary>
        public int Rep(int handle)
        {
            if (!_liveReps.Contains(handle))
                throw new InvalidOperationException(
                    $"Resource handle {handle} not live (already dropped or never allocated).");
            return handle;
        }

        /// <summary>
        /// Drop <paramref name="handle"/>, returning the rep
        /// (== handle in v1) so callers can pass it to the
        /// resource's dtor.
        /// </summary>
        public int Drop(int handle)
        {
            if (handle == InvalidHandle)
                throw new InvalidOperationException(
                    "Cannot drop invalid handle 0.");
            if (!_liveReps.Remove(handle))
                throw new InvalidOperationException(
                    $"Resource handle {handle} not in table (double-drop or never allocated?).");
            return handle;
        }

        /// <summary>True iff <paramref name="handle"/> is currently live.</summary>
        public bool Contains(int handle) => _liveReps.Contains(handle);

        /// <summary>Live handle count — exposed for tests.</summary>
        public int LiveCount => _liveReps.Count;
    }
}
