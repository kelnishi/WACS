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

using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.7. Table Instances
    /// </summary>
    public class TableInstance
    {
        //The actual data array, filled in by InstantiateModule with table.init instructions
        public readonly List<Value> Elements;

        public readonly TableType Type;

        /// <summary>
        /// True when this table is reachable from host threads running concurrently
        /// against the owning runtime. Set at module instantiation time by
        /// <see cref="EnableConcurrentAccess"/> — currently driven by "the module
        /// declares a shared memory" as a threads-1.0 approximation.
        /// Shared-everything-threads will carry a per-declaration shared annotation
        /// that flips this flag directly; the consumer API (<see cref="Grow"/>,
        /// <see cref="Elements"/> readers) is unchanged.
        /// <para>While false, <see cref="Grow"/> skips lock acquisition — zero
        /// overhead for non-threaded modules.</para>
        /// </summary>
        public bool IsShared { get; private set; }

        /// <summary>
        /// @Spec 4.5.3.3. Tables
        /// @Spec 4.5.3.10. Modules
        /// </summary>
        public TableInstance(TableType type, Value refVal)
        {
            Type = (TableType)type.Clone();
            Elements = new List<Value>((int)type.Limits.Minimum);

            for (long i = 0; i < type.Limits.Minimum; i++)
            {
                Elements.Add(refVal);
            }
        }

        /// <summary>
        /// Copy Constructor
        /// </summary>
        /// <param name="type">Clones the type</param>
        /// <param name="elems">Makes a copy of the element list</param>
        private TableInstance(TableType type, IEnumerable<Value> elems) =>
            (Type, Elements) = ((TableType)type.Clone(), elems.ToList());

        public TableInstance Clone() => new(Type, Elements);

        /// <summary>
        /// Mark this table as shared across host threads. Idempotent. Must be
        /// called during instantiation, before any concurrent execution begins.
        /// Layer 2d will wire the grow-time lock used by <see cref="Grow"/>.
        /// </summary>
        internal void EnableConcurrentAccess()
        {
            IsShared = true;
        }

        /// <summary>
        /// @Spec 4.5.3.8. Growing tables
        /// </summary>
        /// <returns>true on succes</returns>
        public bool Grow(long numEntries, Value refInit)
        {
            long len = numEntries + Elements.Count;
            if (len > Constants.MaxTableSize)
                return false;

            var newLimits = new Limits(Type.Limits)
            {
                Minimum = (uint)len
            };
            var validator = TableType.Validator.Limits;
            try
            {
                validator.ValidateAndThrow(newLimits);
            }
            catch (ValidationException exc)
            {
                _ = exc;
                return false;
            }

            Type.Limits = newLimits;
            for (int i = 0; i < numEntries; i++)
            {
                Elements.Add(refInit);
            }

            return true;
        }
    }
}