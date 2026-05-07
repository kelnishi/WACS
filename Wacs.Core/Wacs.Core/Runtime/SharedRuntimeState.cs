// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Wacs.Core.Instructions;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Per-runtime state shared across every <see cref="ExecContext"/> bound to the
    /// runtime. Built up during module instantiation (single-threaded) and
    /// treated as read-only once instantiation completes — the data here is what
    /// future per-thread <see cref="ExecContext"/> instances (Layer 1c) will share
    /// by reference while keeping their own operand stack, frame pool, locals
    /// pool, and call stack private.
    ///
    /// <para>The <see cref="LinkedInstructions"/> sequence is appended to by
    /// <see cref="ExecContext.LinkFunction"/> during module linking; after
    /// <see cref="ExecContext.CacheInstructions"/> runs it is snapshotted into
    /// <see cref="CurrentSequence"/> and no further mutation occurs on the
    /// execution path. The invariant "one writer during init, many readers at
    /// runtime" is what makes sharing safe without locks.</para>
    /// </summary>
    public sealed class SharedRuntimeState
    {
        public readonly Store Store;
        public readonly RuntimeAttributes Attributes;
        public readonly InstructionSequence LinkedInstructions = new();

        /// <summary>
        /// Frozen array snapshot of <see cref="LinkedInstructions"/> produced by
        /// <see cref="ExecContext.CacheInstructions"/>. Null until the first
        /// <c>CacheInstructions</c> call; never written after instantiation.
        /// Hot-path dispatch reads this through <see cref="ExecContext._currentSequence"/>.
        /// </summary>
        public InstructionBase[]? CurrentSequence;

        public SharedRuntimeState(Store store, RuntimeAttributes attributes)
        {
            Store = store;
            Attributes = attributes;
        }
    }
}
