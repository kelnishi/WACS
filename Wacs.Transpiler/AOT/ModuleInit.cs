// Copyright 2025 Kelvin Nishikawa
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
using System.Collections.Generic;
using Wacs.Core.Runtime;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Static helper for module initialization at runtime.
    /// Holds data segment bytes and copies them into memories during Module construction.
    ///
    /// For dynamic assemblies: data is registered at transpile time and accessed at runtime.
    /// For persisted assemblies: data could be read from embedded resources instead.
    /// </summary>
    public static class ModuleInit
    {
        private static readonly Dictionary<int, byte[]> _dataSegments = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Register a data segment's bytes at transpile time.
        /// </summary>
        public static int RegisterDataSegment(byte[] data)
        {
            lock (_lock)
            {
                int id = _dataSegments.Count;
                _dataSegments[id] = data;
                return id;
            }
        }

        /// <summary>
        /// Copy a registered data segment into a memory at the given offset.
        /// Called from the Module constructor's IL.
        /// </summary>
        public static void CopyDataSegment(byte[][] memories, int memIdx, int offset, int segmentId)
        {
            if (!_dataSegments.TryGetValue(segmentId, out var data)) return;
            var memory = memories[memIdx];
            if (offset + data.Length > memory.Length) return; // bounds safety
            Buffer.BlockCopy(data, 0, memory, offset, data.Length);
        }

        /// <summary>
        /// Drop a data segment (replace with empty).
        /// Per WASM spec, dropped segments behave as empty for subsequent memory.init.
        /// </summary>
        public static void DropDataSegment(int segmentId)
        {
            lock (_lock)
            {
                _dataSegments[segmentId] = Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Get a registered data segment's bytes by ID.
        /// </summary>
        public static byte[]? GetDataSegmentData(int segmentId)
        {
            return _dataSegments.TryGetValue(segmentId, out var data) ? data : null;
        }

        // === Element segment storage ===
        // Stores element segment function indices for standalone table.init.
        private static readonly Dictionary<int, Value[]> _elemSegments = new();
        private static readonly object _elemLock = new();

        /// <summary>
        /// Register an element segment's values at transpile time.
        /// Returns the segment ID.
        /// </summary>
        public static int RegisterElemSegment(Value[] values)
        {
            lock (_elemLock)
            {
                int id = _elemSegments.Count;
                _elemSegments[id] = values;
                return id;
            }
        }

        /// <summary>
        /// Get a registered element segment by ID.
        /// Returns null if not found.
        /// </summary>
        public static Value[]? GetElemSegment(int segId)
        {
            return _elemSegments.TryGetValue(segId, out var values) ? values : null;
        }

        /// <summary>
        /// Drop (clear) an element segment by ID.
        /// </summary>
        public static void DropElemSegment(int segId)
        {
            lock (_elemLock)
            {
                _elemSegments[segId] = Array.Empty<Value>();
            }
        }
    }
}
