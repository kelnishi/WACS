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

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Backing storage for <c>MemoryInstance</c> linear memory.
    /// </summary>
    /// <remarks>
    /// Closes the byte[] 2 GiB-cap path from gap 12 of
    /// <c>wasi-nn/WACS-GAPS.md</c>: <see cref="ManagedArray"/> uses
    /// the historical <c>byte[]</c> + <c>Array.Resize</c> backing
    /// (subject to <c>Array.MaxLength ≈ 2^31-1</c>);
    /// <see cref="NativePointer"/> uses raw native memory allocated
    /// via <c>NativeMemory.AllocZeroed</c> (.NET 6+) or
    /// <c>Marshal.AllocHGlobal</c> + zeroing fallback, removing the
    /// 2 GiB cap and enabling memory64. The runtime defaults to
    /// <see cref="ManagedArray"/> until the migration phases land.
    /// </remarks>
    public enum MemoryStorageMode
    {
        /// <summary>Managed <c>byte[]</c> via <c>Array.Resize</c>.
        /// Hard-capped at ~2 GiB (Array.MaxLength).</summary>
        ManagedArray = 0,

        /// <summary>Native pointer + <c>nuint</c> length via
        /// <c>NativeMemory.AllocZeroed</c>. No 2 GiB cap; required
        /// for memory64. Caller is responsible for disposing the
        /// owning <c>WasmRuntime</c> / <c>MemoryInstance</c> so the
        /// native buffer is freed.</summary>
        NativePointer = 1,
    }

    public class RuntimeOptions
    {
        public bool SkipModuleValidation = false;
        public bool SkipStartFunction = false;
        public bool TimeInstantiation = false;

        /// <summary>
        /// Backing storage for new <c>MemoryInstance</c> allocations.
        /// Default <see cref="MemoryStorageMode.ManagedArray"/> keeps
        /// the existing byte[] path byte-stable for callers that
        /// haven't opted in. Set to
        /// <see cref="MemoryStorageMode.NativePointer"/> to allocate
        /// native memory and lift the 2 GiB cap.
        /// </summary>
        public MemoryStorageMode MemoryStorage = MemoryStorageMode.ManagedArray;
    }
}