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

using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Module-local allocator for <see cref="ContInstance"/>
    /// values, parallel to <c>Store</c>'s continuation list but
    /// usable in standalone (no-WasmRuntime) mode. Mixed-mode
    /// transpiled modules can either go through this store or
    /// through the WasmRuntime's Store; helpers branch based on
    /// what's available.
    ///
    /// <para>The allocator mints a unique idx per call but does
    /// NOT retain a strong reference to the instance — wasm refs
    /// reach the cont through the <c>Value.GcRef</c> they carry,
    /// so once those refs become unreachable the cont can be
    /// reclaimed by the CLR GC. Lookup-by-idx is unsupported in
    /// standalone (no consumer needs it); keep the store
    /// retention-free so 1M+ resume cycles don't grow the heap
    /// unboundedly.</para>
    /// </summary>
    public sealed class ContinuationStore
    {
        private int _nextIdx;

        public ContInstance Allocate(TypeIdx contTypeIdx, FuncAddr func) =>
            new ContInstance(_nextIdx++, contTypeIdx, func);

        public ContInstance Allocate(TypeIdx contTypeIdx, Delegate standaloneDelegate) =>
            new ContInstance(_nextIdx++, contTypeIdx, standaloneDelegate);
    }
}
