// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Component Model async <c>subtask</c> primitive — a fork of
    /// a parent <see cref="ComponentTask"/>. Spawned by an async
    /// canon-lowered call from inside a parent task body; the
    /// subtask handle lives in the parent's handle space until
    /// <c>subtask.drop</c> reclaims it.
    ///
    /// <para>Holds references to both the spawned child task and
    /// its parent for cancellation propagation
    /// (<c>subtask.cancel async?</c> can cascade upward when the
    /// async flag indicates a synchronous-wait pattern, or just
    /// signal the child when async). The dispatcher applies the
    /// semantics — this class is the data carrier.</para>
    /// </summary>
    public sealed class ComponentSubtask
    {
        /// <summary>Handle this subtask was allocated under.</summary>
        public int Handle { get; }

        /// <summary>The forked child task.</summary>
        public ComponentTask Child { get; }

        /// <summary>The task that performed the async lower spawn.</summary>
        public ComponentTask Parent { get; }

        public ComponentSubtask(int handle, ComponentTask child, ComponentTask parent)
        {
            Handle = handle;
            Child = child;
            Parent = parent;
        }
    }
}
