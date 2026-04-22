// Copyright 2024 Kelvin Nishikawa
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

using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.9. Global Instances
    /// </summary>
    public class GlobalInstance
    {
        public readonly GlobalType Type;
        private Value _value;

        /// <summary>
        /// Per-instance lock used to serialize reads and writes of
        /// <see cref="Value"/> on shared globals. Null until
        /// <see cref="EnableConcurrentAccess"/> fires — non-shared globals pay
        /// nothing on the hot path.
        ///
        /// <para>Why a lock at all: <see cref="Value"/> is a 24-byte struct
        /// (ValType + DUnion + GcRef). A plain field assignment is not atomic on
        /// any supported platform — concurrent writer and reader can observe a
        /// mix of bytes from two different values (torn read). Future: narrow
        /// numeric globals to <see cref="Volatile.Read{T}"/> on the 8-byte data
        /// union for a lock-free fast path; v128 and ref-typed globals stay
        /// under lock.</para>
        /// </summary>
        private object? _lock;

        /// <summary>
        /// True when this global is reachable from host threads running concurrently
        /// against the owning runtime. Set by
        /// <see cref="EnableConcurrentAccess"/> at module instantiation time —
        /// currently driven by "the module declares a shared memory" as a
        /// threads-1.0 approximation; shared-everything-threads will flip this
        /// per-declaration when that proposal lands.
        /// <para>While false, the hot-path <see cref="Value"/> getter/setter bypass
        /// the synchronization machinery entirely — non-threaded modules pay
        /// nothing.</para>
        /// </summary>
        public bool IsShared { get; private set; }

        public GlobalInstance(GlobalType type, Value initialValue)
        {
            Type = type;
            _value = initialValue;
        }

        /// <summary>
        /// Mark this global as shared across host threads. Idempotent. Must be
        /// called during instantiation, before any concurrent execution can begin.
        /// </summary>
        internal void EnableConcurrentAccess()
        {
            if (_lock == null)
            {
                _lock = new object();
                IsShared = true;
            }
        }

        public Value Value
        {
            get
            {
                var l = _lock;
                if (l == null) return _value;
                lock (l) return _value;
            }
            set
            {
                if (Type.Mutability == Mutability.Immutable)
                    throw new InvalidOperationException("Global is immutable");
                var l = _lock;
                if (l == null)
                {
                    _value = value;
                }
                else
                {
                    lock (l) _value = value;
                }
            }
        }
    }
}