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

using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Extension point for CIL emission of the WebAssembly Stack
    /// Switching opcodes (<c>cont.new</c>, <c>cont.bind</c>,
    /// <c>suspend</c>, <c>resume</c>, <c>resume_throw</c>,
    /// <c>switch</c>).
    ///
    /// <para>No CIL is emitted today: <see cref="CanEmit"/> returns
    /// <c>false</c> for the six opcodes, so any function containing
    /// them is routed to the interpreter via the
    /// <c>FunctionCodegen.CanEmitAllInstructions</c> guard. The
    /// interpreter's <c>SuspensionDispatcher</c> handles the
    /// runtime semantics.</para>
    ///
    /// <para><strong>Mixed-mode only.</strong> The fallback path
    /// requires a live <c>ThinContext.ExecContext</c> — present
    /// when the transpiled module is invoked through a
    /// <c>WasmRuntime</c>'s stack invoker (mixed-mode). In
    /// standalone mode (the transpiled <c>Module</c> class
    /// instantiated via <c>Activator.CreateInstance</c> without a
    /// runtime context), <c>CallEmitter.InvokeFallback</c> throws
    /// <c>NotSupportedException("Function N not transpiled and no
    /// interpreter available")</c>. Until CIL emission lands for
    /// the six opcodes, cont.*-bearing modules must be hosted by
    /// a WasmRuntime to be callable.</para>
    ///
    /// <para>A real emit pass would CIL-call out to runtime
    /// helpers (the existing <c>SuspensionDispatcher.TryHandle</c>,
    /// <c>Store.AllocateContinuation</c>, and a new helper for the
    /// suspend / branch unwind) rather than try to express
    /// non-local control transfer in CIL directly. That preserves
    /// the no-fiber-API requirement: the transpiler emits straight-
    /// line IL plus method calls into <c>Wacs.Core</c>, never
    /// touching <c>System.Threading.Fiber</c> or async-state-
    /// machine internals.</para>
    /// </summary>
    internal static class StackSwitchingEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            // Reserve the predicate slot so additions land in one
            // place when CIL emission for the six opcodes ships.
            // Returning false routes cont.* containing functions
            // through the interpreter fallback path.
            return false;
        }

        /// <summary>
        /// True iff <paramref name="op"/> is one of the six Stack
        /// Switching opcodes. Distinct from <see cref="CanEmit"/>
        /// so callers can distinguish "we know this opcode but
        /// can't emit it" (interpreter fallback intentional) from
        /// "we don't recognize this opcode at all" (genuine
        /// unsupported instruction).
        /// </summary>
        public static bool IsStackSwitchingOpcode(WasmOpCode op)
        {
            return op == WasmOpCode.ContNew
                   || op == WasmOpCode.ContBind
                   || op == WasmOpCode.Suspend
                   || op == WasmOpCode.Resume
                   || op == WasmOpCode.ResumeThrow
                   || op == WasmOpCode.Switch;
        }
    }
}
