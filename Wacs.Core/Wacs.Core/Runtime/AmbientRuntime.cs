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

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Ambient runtime pins read by every WACS module-instantiation
    /// path — the interpreter's <c>ComponentInstance.Instantiate</c>,
    /// the transpiler's <c>InitializationHelper</c>, and the
    /// AotLinked module-init IL emission. Hosts pin a value here
    /// before instantiation; any of those paths can read it.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Wacs.Core</c> so packages above it
    /// (<c>Wacs.ComponentModel</c>, <c>Wacs.Transpiler.Lib</c>) can
    /// share the same source of truth without a circular reference.
    /// Static-flag dispatch is intentionally simple: a single
    /// transpile-and-dispatch sequence is single-threaded, the flag
    /// is set just before instantiation and not inspected afterwards.
    /// <c>AsyncLocal&lt;T&gt;</c> would be the right tool if concurrent
    /// instantiation with mixed modes ever became a use case.
    /// </remarks>
    public static class AmbientRuntime
    {
        /// <summary>
        /// Storage backing for memories the next instantiated module
        /// allocates. Defaults to
        /// <see cref="MemoryStorageMode.ManagedArray"/>.
        /// </summary>
        public static MemoryStorageMode MemoryStorage = MemoryStorageMode.ManagedArray;
    }
}
