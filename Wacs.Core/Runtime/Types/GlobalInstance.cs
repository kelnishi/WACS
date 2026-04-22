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
        /// Layer 2c will wire the per-instance lock used by the Value property.
        /// </summary>
        internal void EnableConcurrentAccess()
        {
            IsShared = true;
        }

        public Value Value
        {
            get => _value;
            set {
                if (Type.Mutability == Mutability.Immutable)
                    throw new InvalidOperationException("Global is immutable");
                _value = value;
            }
        }
    }
}