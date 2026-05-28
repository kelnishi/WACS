// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Lifecycle state of a Component Model async <c>task</c>.
    /// Mirrors the spec's transitions: created → running →
    /// returned / cancelled / failed (terminal).
    /// </summary>
    public enum ComponentTaskState
    {
        /// <summary>Allocated but its body hasn't entered the wasm frame yet.</summary>
        Starting,

        /// <summary>Body has entered; suspends and resumes keep it here.</summary>
        Started,

        /// <summary><c>task.return</c> ran — results materialized on
        /// <see cref="ComponentTask.Completion"/>.</summary>
        Returned,

        /// <summary><c>task.cancel</c> ran. <c>Completion</c> is set
        /// canceled.</summary>
        Cancelled,

        /// <summary>An exception escaped the task body without
        /// being caught. <c>Completion</c> is set faulted.</summary>
        Failed,
    }
}
