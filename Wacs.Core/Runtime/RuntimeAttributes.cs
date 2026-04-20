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

using Wacs.Core.Instructions;

namespace Wacs.Core.Runtime
{
    public class RuntimeAttributes
    {
        public int GrowCallStack = 512;

        public int InitialCallStack = 512;
        public bool Live = true;
        public int LocalPoolSize = 64;
        public int MaxCallStack = 2048;

        /// <summary>
        /// Legacy field — unused after phase M. Retained so existing public-API
        /// consumers that read/write this attribute still compile. Phase M replaced
        /// the native-recursion call model (each WASM <c>call</c> adding three native
        /// frames and bounded by this counter) with an explicit heap-allocated
        /// <c>ctx._switchCallStack</c>. Depth is now bounded by
        /// <see cref="MaxCallStack"/> (default 2048), identical to the polymorphic
        /// path, and costs zero native thread stack per level.
        /// </summary>
        [System.Obsolete("Unused after phase M — switch runtime no longer uses native recursion. Use MaxCallStack instead.")]
        public int SwitchMaxCallStack = 2048;

        /// <summary>
        /// When true, <see cref="Wacs.Core.Compilation.BytecodeCompiler.Compile"/>
        /// runs <see cref="Wacs.Core.Compilation.StreamFusePass"/> as its final step:
        /// common 2- and 3-op wasm sequences (local.get + local.set, i32.const +
        /// local.set, local.get + i32.const + i32.add, etc.) are rewritten into
        /// single WacsCode super-ops. Each match saves one or two switch-dispatches
        /// per execution and shrinks the stream by 1–2 bytes per fuse.
        ///
        /// <para>Off by default — the switch runtime executes the unfused stream
        /// with identical semantics, and the fuse pass adds non-trivial compile-time
        /// work (one walk over the function's bytecode per Compile). Flip on per
        /// runtime for benchmark runs and production-style workloads.</para>
        /// </summary>
        public bool UseSwitchSuperInstructions = false;

        public int MaxFunctionLocals = 2048;

        public int MaxOpStack = 2048;
        public InstructionBaseFactory InstructionFactory { get; set; } = SpecFactory.Factory;
    }

}