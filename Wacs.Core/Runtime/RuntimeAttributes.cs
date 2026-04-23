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
using Wacs.Core.Runtime.Concurrency;

namespace Wacs.Core.Runtime
{
    public class RuntimeAttributes
    {
        /// <summary>
        /// Backend for <c>memory.atomic.wait*</c> / <c>memory.atomic.notify</c>
        /// from the WebAssembly threads proposal. Defaults to
        /// <see cref="NotSupportedPolicy"/> when the runtime detects a
        /// Unity IL2CPP environment (main-thread blocking risks), and
        /// <see cref="HostDefinedPolicy"/> elsewhere. Hosts may override
        /// at any time before instantiation.
        /// </summary>
        public IConcurrencyPolicy ConcurrencyPolicy { get; set; } =
            DetectIsUnity() ? (IConcurrencyPolicy)new NotSupportedPolicy()
                            : new HostDefinedPolicy();

        /// <summary>
        /// When true, validation downgrades the "atomic op on non-shared
        /// memory" error to a pass. Emscripten and LLVM sometimes emit
        /// atomic ops on non-shared memories for single-threaded hosts; the
        /// threads proposal itself requires shared. Default false (strict).
        /// </summary>
        public bool RelaxAtomicSharedCheck { get; set; } = false;

        /// <summary>
        /// Enable parsing + validation of the shared-everything-threads
        /// Phase-1 proposal subset (Layer 5): <c>shared</c> annotations on
        /// globals and tables, thread-local globals, <c>global.atomic.*</c>
        /// opcodes, and <c>pause</c>.
        ///
        /// <para>Default <c>false</c> so baseline wasm binaries reject the
        /// new encoding bits as invalid — the proposal is pre-standard
        /// (Phase 1 at time of writing, github.com/WebAssembly/shared-everything-threads)
        /// and its binary/text format may shift before standardization.
        /// Opt-in per runtime when working with shared-everything-aware
        /// toolchains.</para>
        ///
        /// <para>Does <em>not</em> enable Component-Model canonical
        /// builtins like <c>thread.spawn_ref</c> — those ship via a
        /// future Component Threads adapter, not at the core wasm level.</para>
        /// </summary>
        public bool EnableSharedEverythingThreads { get; set; } = false;

        // Cache the Unity detection result to avoid reflecting on every
        // new RuntimeAttributes().
        private static readonly bool _isUnity = ProbeUnity();
        private static bool DetectIsUnity() => _isUnity;
        private static bool ProbeUnity()
        {
            // Type.GetType with throwOnError:false is AOT-safe under trimmer
            // + IL2CPP. UnityEngine.Application is never stripped from a
            // Unity build (it's bootstrap-critical).
            var t = System.Type.GetType(
                "UnityEngine.Application, UnityEngine.CoreModule",
                throwOnError: false);
            return t != null;
        }

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