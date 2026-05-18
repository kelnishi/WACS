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
using System.Collections.Generic;
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
    /// </summary>
    public sealed class ContinuationStore
    {
        private readonly List<ContInstance> _instances = new();

        public ContInstance Allocate(TypeIdx contTypeIdx, FuncAddr func)
        {
            var inst = new ContInstance(_instances.Count, contTypeIdx, func);
            _instances.Add(inst);
            return inst;
        }

        public ContInstance Allocate(TypeIdx contTypeIdx, Delegate standaloneDelegate)
        {
            var inst = new ContInstance(_instances.Count, contTypeIdx, standaloneDelegate);
            _instances.Add(inst);
            return inst;
        }

        public ContInstance? Get(long idx) =>
            idx >= 0 && idx < _instances.Count ? _instances[(int)idx] : null;
    }
}
