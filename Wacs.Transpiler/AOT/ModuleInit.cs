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
    }
}
