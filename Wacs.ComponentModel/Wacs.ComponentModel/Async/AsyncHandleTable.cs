// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Typed facade over the <see cref="AsyncDispatcher"/>'s shared
    /// per-component handle store. Spec-aligned: the Component
    /// Model's <c>task</c>, <c>subtask</c>, <c>waitable-set</c>,
    /// <c>stream</c>, <c>future</c>, and <c>error-context</c>
    /// handles all live in a single per-component namespace —
    /// the same integer never refers to two different things at
    /// once. Each facade tags allocations with its
    /// <see cref="WaitableKind"/> and treats handles registered
    /// under a different kind as absent (matching the spec's
    /// kind-discriminated lookup).
    ///
    /// <para>Constructed only by <see cref="AsyncDispatcher"/>;
    /// embedders access existing facades through the dispatcher's
    /// typed properties (<see cref="AsyncDispatcher.Tasks"/>,
    /// <see cref="AsyncDispatcher.Futures"/>, etc.).</para>
    ///
    /// <para><b>Wire convention:</b> handles are 4-byte positive
    /// integers. Handle <c>0</c> is reserved as the null/no-handle
    /// sentinel — the store never allocates it. Freelist recycling
    /// at the store level reuses dropped handles across all kinds
    /// so long-running components don't drift their counter
    /// unbounded.</para>
    ///
    /// <para><b>Thread-safety:</b> not thread-safe by design. The
    /// store assumes one wasm execution context calls into it at
    /// a time — true for the stackful CM async proposal (Phase 3
    /// target) and the non-shared <c>thread.*</c> canon ops
    /// (0x26–0x2b, 🧵 in the spec). The explicit-threads shared
    /// variants (🧵② 0x40–0x42) are rejected at the parser today;
    /// concurrent storage would be needed if they ever land.</para>
    /// </summary>
    public sealed class AsyncHandleTable<T> where T : class
    {
        private readonly AsyncDispatcher _owner;
        private readonly WaitableKind _kind;

        internal AsyncHandleTable(AsyncDispatcher owner, WaitableKind kind)
        {
            _owner = owner;
            _kind = kind;
        }

        /// <summary>Kind of waitable this facade allocates / lookups.</summary>
        public WaitableKind Kind => _kind;

        /// <summary>Allocate a fresh handle from the dispatcher's
        /// shared counter and bind <paramref name="value"/> under
        /// this facade's kind tag.</summary>
        public int Allocate(T value) =>
            _owner.AllocateHandleInternal(_kind, value);

        /// <summary>
        /// Allocate a fresh handle and let
        /// <paramref name="factory"/> compute the value from the
        /// freshly-minted handle. Used when the stored value
        /// (e.g. <see cref="ComponentTask"/>,
        /// <see cref="ComponentWaitableSet"/>) carries its handle
        /// as a field — avoids the allocate-then-rewrite dance.
        /// </summary>
        public int Allocate(Func<int, T> factory) =>
            _owner.AllocateHandleInternalWithFactory(_kind, factory);

        /// <summary>Lookup; returns null when the handle was never
        /// allocated, has been dropped, OR is currently registered
        /// under a different kind (matching the spec's
        /// kind-discriminated lookup).</summary>
        public T? Get(int handle) =>
            _owner.GetHandleInternal<T>(handle, _kind);

        /// <summary>
        /// Release the slot back to the dispatcher's shared
        /// freelist. Returns the dropped value (so callers can
        /// run a release callback on it) or null when the handle
        /// was already absent / wrong kind.
        /// </summary>
        public T? Drop(int handle) =>
            _owner.DropHandleInternal<T>(handle, _kind);

        /// <summary>True iff <paramref name="handle"/> currently
        /// maps to a live value of this facade's kind. False on
        /// kind mismatch (the handle may live in a different
        /// facade).</summary>
        public bool Contains(int handle) =>
            _owner.HandleKindInternal(handle) == _kind;

        /// <summary>Live entry count for this facade's kind. O(N)
        /// in the dispatcher's total handle population — embedders
        /// shouldn't rely on this on a hot path.</summary>
        public int Count => _owner.CountByKindInternal(_kind);
    }
}
