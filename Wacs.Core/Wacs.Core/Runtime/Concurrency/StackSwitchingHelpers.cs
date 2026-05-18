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
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Static runtime entry points for the Stack Switching opcodes
    /// called from CIL emitted by the WACS transpiler. Each helper
    /// is the morally-identical operation that the corresponding
    /// <c>Inst*.Execute</c> performs, restructured to take typed
    /// CLR arguments (instead of popping from the interpreter's
    /// OpStack) and to be safely callable from emitted IL.
    ///
    /// <para>All helpers require a live <see cref="ExecContext"/>
    /// (mixed-mode hosting via a <see cref="WasmRuntime"/>). In
    /// standalone-mode transpiled modules — where <c>ExecContext</c>
    /// is null — they throw <see cref="NotSupportedException"/>
    /// with an explanatory message. A standalone-friendly
    /// continuation runtime (Module-local ContinuationStore + a
    /// delegate-based function-dispatch path) is a separate
    /// design effort.</para>
    /// </summary>
    public static class StackSwitchingHelpers
    {
        private const string NoHostContextMessage =
            "Stack switching opcodes (cont.*) require a WasmRuntime host context " +
            "for runtime support. Standalone-mode transpiled modules do not yet " +
            "include a self-contained continuation runtime.";

        private static ExecContext RequireExec(ExecContext? exec)
        {
            if (exec == null)
                throw new NotSupportedException(NoHostContextMessage);
            return exec;
        }

        /// <summary>
        /// <c>cont.new $ct</c>: pop a function reference, allocate
        /// a Fresh continuation tied to that function, push a
        /// non-nullable continuation ref.
        /// </summary>
        public static Value ContNew(ExecContext? exec, int typeIdx, Value funcRef)
        {
            var ctx = RequireExec(exec);
            if (funcRef.IsNullRef)
                throw new TrapException("cont.new: null function reference.");
            var funcAddr = funcRef.GetFuncAddr(ctx.Frame.Module.Types);
            if (!ctx.Store.Contains(funcAddr))
                throw new TrapException(
                    $"cont.new: function address {funcAddr} is not in the store.");
            var contInst = ctx.Store.AllocateContinuation((TypeIdx)typeIdx, funcAddr);
            return new Value(ValType.ContRefNN, contInst);
        }

        /// <summary>
        /// <c>cont.bind $ct1 $ct2</c>: pop a Fresh source
        /// continuation and a prefix-args tuple, allocate a new
        /// continuation typed as $ct2 with the prefix args bound,
        /// mark the source Completed, push the new continuation.
        /// <paramref name="prefixArgs"/> must be in source-stack
        /// order (leftmost first); the emitter is responsible for
        /// packing the CIL stack values into the array in the
        /// right order.
        /// </summary>
        public static Value ContBind(
            ExecContext? exec, int targetTypeIdx,
            Value contRef, Value[] prefixArgs)
        {
            var ctx = RequireExec(exec);
            if (contRef.IsNullRef)
                throw new TrapException("cont.bind: null continuation reference.");
            var source = contRef.GcRef as ContInstance
                         ?? throw new TrapException(
                             "cont.bind: ref does not point to a continuation.");
            if (source.State != ContState.Fresh)
                throw new TrapException(
                    $"cont.bind: continuation is not fresh (state {source.State}).");

            var bound = ctx.Store.AllocateContinuation(
                (TypeIdx)targetTypeIdx, source.Function);
            bound.BoundArgs.AddRange(source.BoundArgs);
            bound.BoundArgs.AddRange(prefixArgs);
            source.State = ContState.Completed;
            return new Value(ValType.ContRefNN, bound);
        }

        /// <summary>
        /// <c>suspend $tag</c>: throw a SuspensionException
        /// carrying the tag's address and the payload values, in
        /// the order that the interpreter's PopResults would have
        /// produced (last-pushed first when iterating the
        /// returned Stack).
        /// </summary>
        public static void Suspend(ExecContext? exec, int tagIdxValue, Value[] payload)
        {
            var ctx = RequireExec(exec);
            var tagIdx = (TagIdx)(uint)tagIdxValue;
            if (!ctx.Frame.Module.TagAddrs.Contains(tagIdx))
                throw new TrapException(
                    $"suspend: tag {tagIdx} not addressable in this frame's module.");
            var tagAddr = ctx.Frame.Module.TagAddrs[tagIdx];
            if (!ctx.Store.Contains(tagAddr))
                throw new TrapException(
                    $"suspend: tag address {tagAddr} is not in the store.");

            // Build a stack of payload values matching the order
            // the interpreter would have left them after
            // PopResults (last pushed by guest = top of stack).
            var stack = new Stack<Value>();
            for (int i = 0; i < payload.Length; i++)
                stack.Push(payload[i]);
            throw new SuspensionException(tagAddr, stack);
        }
    }
}
