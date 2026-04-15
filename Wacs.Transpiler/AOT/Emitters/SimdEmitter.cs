// Copyright 2025 Kelvin Nishikawa
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

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for 0xFD prefix (SIMD) instructions.
    ///
    /// All SIMD ops dispatch through the interpreter's Execute methods via
    /// ExecContext.OpStack marshaling. This reuses the interpreter's spec-compliant
    /// scalar implementations as the reference.
    ///
    /// The instruction's StackDiff property determines how many values to pop/push.
    ///
    /// Future: intrinsics-based implementations can be added per-opcode by replacing
    /// specific cases with direct Vector128 operations, gated by transpiler options.
    /// </summary>
    internal static class SimdEmitter
    {
        internal static readonly List<InstructionBase> InstructionRegistry = new();
        private static readonly object _lock = new();

        private static int RegisterInstruction(InstructionBase inst)
        {
            lock (_lock)
            {
                int id = InstructionRegistry.Count;
                InstructionRegistry.Add(inst);
                return id;
            }
        }

        public static bool CanEmit(SimdCode op) => true;

        /// <summary>
        /// Emit a SIMD instruction by delegating to the interpreter.
        ///
        /// Uses StackDiff to determine marshaling:
        ///   StackDiff = outputCount - inputCount
        ///   Stores: outputCount = 0. Everything else: outputCount = 1.
        ///   inputCount = outputCount - StackDiff
        /// </summary>
        public static void Emit(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            bool isStore = op == SimdCode.V128Store ||
                           op == SimdCode.V128Store8Lane ||
                           op == SimdCode.V128Store16Lane ||
                           op == SimdCode.V128Store32Lane ||
                           op == SimdCode.V128Store64Lane;
            int outputCount = isStore ? 0 : 1;
            int inputCount = outputCount - inst.StackDiff;
            if (inputCount < 0) inputCount = 0;

            int instId = RegisterInstruction(inst);

            // Spill inputs from CIL stack
            var inputLocals = new LocalBuilder[inputCount];
            for (int i = inputCount - 1; i >= 0; i--)
            {
                inputLocals[i] = il.DeclareLocal(typeof(Value));
                il.Emit(OpCodes.Stloc, inputLocals[i]);
            }

            // Push inputs to OpStack
            for (int i = 0; i < inputCount; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, inputLocals[i]);
                il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                    nameof(SimdDispatch.PushValue), BindingFlags.Public | BindingFlags.Static)!);
            }

            // Execute via interpreter
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, instId);
            il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                nameof(SimdDispatch.ExecuteSimdOp), BindingFlags.Public | BindingFlags.Static)!);

            // Pop results from OpStack
            for (int i = 0; i < outputCount; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                    nameof(SimdDispatch.PopValue), BindingFlags.Public | BindingFlags.Static)!);
            }
        }
    }

    /// <summary>
    /// Runtime dispatch for SIMD operations.
    /// Marshals between CIL stack (Value) and interpreter OpStack.
    /// Uses the interpreter's spec-compliant implementations as the reference.
    /// </summary>
    public static class SimdDispatch
    {
        public static void PushValue(TranspiledContext ctx, Value val)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            ctx.ExecContext.OpStack.PushValue(val);
        }

        public static Value PopValue(TranspiledContext ctx)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            return ctx.ExecContext.OpStack.PopAny();
        }

        public static void ExecuteSimdOp(TranspiledContext ctx, int instructionId)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            var inst = SimdEmitter.InstructionRegistry[instructionId];
            inst.Execute(ctx.ExecContext);
        }
    }
}
