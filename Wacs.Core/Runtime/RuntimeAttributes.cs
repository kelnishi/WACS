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
        /// calls — each <c>call</c> opcode adds three managed frames (InvokeWasm's,
        /// <c>SwitchRuntime.Run</c>'s, and the generated <c>GeneratedDispatcher.Run</c>'s).
        ///
        /// <para>The generated Run method frame is sizable because the method body is a
        /// ~300-case switch — even with no register-bank bloat, RyuJIT allocates a few
        /// hundred bytes of locals. 256 fits comfortably within the ~1 MiB default
        /// .NET thread stack (used by xUnit test threads) while still catching any
        /// runaway recursion as a clean <c>WasmRuntimeException</c>. If your embedding
        /// needs deeper WASM call chains, tune this alongside <c>Thread.StackSize</c>
        /// on the invoking thread. Polymorphic path is unchanged.</para>
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