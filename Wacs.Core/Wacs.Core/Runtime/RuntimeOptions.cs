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
    /// Backing storage for a <see cref="Wacs.Core.Runtime.Types.MemoryInstance"/>'s
    /// linear-memory bytes. Selected per runtime via
    /// <see cref="RuntimeOptions.MemoryStorage"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ManagedArray"/> uses a <c>byte[]</c> grown via
    /// <c>Array.Resize</c>: simple, GC-managed, and capped at
    /// <c>Array.MaxLength ≈ 2^31-1</c> (~2 GiB).
    /// <see cref="NativePointer"/> uses native memory allocated via
    /// <c>NativeMemory.AllocZeroed</c> (.NET 6+) or
    /// <c>Marshal.AllocHGlobal</c> + zeroing fallback (legacy): no
    /// 2 GiB cap, supports the wasm32 spec's full 4 GiB, and is
    /// required for memory64 modules. Native-mode memory must be
    /// disposed by tearing down the owning <see cref="MemoryInstance"/>
    /// — finalizer is a backstop only.
    /// </remarks>
    public enum MemoryStorageMode
    {
        /// <summary>Managed <c>byte[]</c> via <c>Array.Resize</c>.
        /// Hard-capped at ~2 GiB (Array.MaxLength).</summary>
        ManagedArray = 0,

        /// <summary>Native pointer + <c>nuint</c> length. No 2 GiB
        /// cap; required for memory64. Owning
        /// <see cref="Wacs.Core.Runtime.Types.MemoryInstance"/>
        /// must be disposed to free the native buffer.</summary>
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