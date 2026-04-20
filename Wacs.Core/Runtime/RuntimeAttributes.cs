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
        /// Upper bound on nested <c>InvokeWasm</c> depth on the switch-runtime path
        /// specifically. Guards the managed C# stack from overflowing recursive WASM
        /// calls — each <c>call</c> opcode adds at least three managed frames
        /// (InvokeWasm's, <c>SwitchRuntime.Run</c>'s, and the generated
        /// <c>GeneratedDispatcher.Run</c>'s), plus one more if the call site dispatches
        /// through a prefix sub-method (<c>DispatchFC</c> / <c>DispatchFF</c> / etc.).
        ///
        /// <para>Sizing: static method prologues measure only ≈800 B per WASM call
        /// level on ARM64. Empirically the budget is closer to 6–8 KiB per level once
        /// the JIT's dynamic spill slots, passed-in ReadOnlySpan parameters, and the
        /// test runner's own overhead are included — a managed StackOverflowException
        /// fires on xUnit's default thread stack at depth ≈128 even though the
        /// static-measured cost would suggest 1024+. 48 is the empirical ceiling that
        /// reliably turns a runaway (<c>call.wast</c>'s <c>runaway</c> /
        /// <c>mutual-runaway</c>) into a clean <c>WasmRuntimeException</c> before
        /// the CLR terminates the test host.</para>
        ///
        /// <para>Known spec-test consequence: the three <c>even</c>/<c>odd</c>
        /// mutual-recursion tests in <c>call.wast</c> / <c>call_indirect.wast</c> /
        /// <c>call_ref.wast</c> require depths ~100–201 and therefore trap under the
        /// switch runtime with the default limit. The polymorphic runtime passes them
        /// because it uses an explicit <c>Stack&lt;Frame&gt;</c> rather than native
        /// recursion (see <see cref="MaxCallStack"/> = 2048) — its depth budget costs
        /// zero native stack per level. Embeddings that need deeper WASM call chains
        /// under the switch runtime must spawn a worker thread with an enlarged
        /// <c>Thread.StackSize</c> (pre-phase-A we used a dedicated 32-MiB worker
        /// thread for exactly this reason) and raise this attribute accordingly.</para>
        /// </summary>
        public int SwitchMaxCallStack = 48;

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