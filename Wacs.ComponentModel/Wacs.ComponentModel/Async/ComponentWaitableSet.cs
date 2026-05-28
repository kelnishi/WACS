// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Component Model async <c>waitable-set</c> primitive — a
    /// collection of waitable handles (subtasks, streams,
    /// futures) that a guest can <c>waitable-set.wait</c> or
    /// <c>waitable-set.poll</c> on. Members are added via
    /// <c>waitable.join</c>; the dispatcher's wait/poll picks
    /// any member that has reached a deliverable state.
    ///
    /// <para>Members are stored as opaque handle integers; the
    /// dispatcher cross-references them against the appropriate
    /// per-kind table (task / subtask / stream / future) when
    /// resolving the wait. Duplicate joins are no-ops — the set
    /// semantics are by handle identity.</para>
    /// </summary>
    public sealed class ComponentWaitableSet
    {
        /// <summary>Handle this waitable-set was allocated under.</summary>
        public int Handle { get; }

        private readonly HashSet<int> _members = new HashSet<int>();

        public ComponentWaitableSet(int handle) { Handle = handle; }

        /// <summary>Add <paramref name="waitable"/> to the set. Idempotent.</summary>
        public void Join(int waitable) => _members.Add(waitable);

        /// <summary>Remove <paramref name="waitable"/> from the set. Returns true iff it was present.</summary>
        public bool Remove(int waitable) => _members.Remove(waitable);

        /// <summary>True iff the set currently includes <paramref name="waitable"/>.</summary>
        public bool Contains(int waitable) => _members.Contains(waitable);

        /// <summary>Read-only snapshot of joined waitables.</summary>
        public IReadOnlyCollection<int> Members => _members;

        /// <summary>Live member count.</summary>
        public int Count => _members.Count;
    }
}
