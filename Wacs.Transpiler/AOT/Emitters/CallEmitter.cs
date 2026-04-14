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

using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly call instructions.
    ///
    /// For intra-module calls to sibling transpiled functions:
    /// The WASM stack has [param0, param1, ...] on top. The CIL method expects
    /// (TranspiledContext ctx, param0, param1, ...). We need to insert ctx
    /// underneath the params by spilling them to temp locals.
    ///
    /// Phase 3 handles: call (direct intra-module)
    /// Deferred: call_indirect, call_ref, return_call variants
    /// </summary>
    internal static class CallEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.Call;
        }

        /// <summary>
        /// Emit a direct call to a sibling function within the same module.
        /// </summary>
        /// <param name="il">IL generator</param>
        /// <param name="inst">The call instruction (has FuncIdx X)</param>
        /// <param name="siblingFunctions">FunctionInstance array for locally-defined functions</param>
        /// <param name="siblingMethods">MethodBuilder array for locally-defined functions</param>
        /// <param name="importCount">Number of imported functions (offset into FuncAddrs)</param>
        public static void EmitCall(
            ILGenerator il,
            InstCall inst,
            FunctionInstance[] siblingFunctions,
            MethodBuilder[] siblingMethods,
            int importCount)
        {
            int funcIdx = (int)inst.X.Value;

            if (funcIdx < importCount)
            {
                throw new TranspilerException(
                    $"CallEmitter: calls to imported functions not yet supported (funcIdx={funcIdx})");
            }

            int localIdx = funcIdx - importCount;
            if (localIdx < 0 || localIdx >= siblingMethods.Length)
            {
                throw new TranspilerException(
                    $"CallEmitter: function index {funcIdx} out of range (imports={importCount}, locals={siblingMethods.Length})");
            }

            var targetMethod = siblingMethods[localIdx];
            var calleeType = siblingFunctions[localIdx].Type;
            int wasmParamCount = calleeType.ParameterTypes.Arity;

            if (wasmParamCount == 0)
            {
                il.Emit(OpCodes.Ldarg_0); // TranspiledContext
                il.Emit(OpCodes.Call, targetMethod);
            }
            else
            {
                // Spill WASM params to temps, insert ctx underneath, push back
                var paramTypes = calleeType!.ParameterTypes.Types;
                var temps = new LocalBuilder[wasmParamCount];
                for (int i = wasmParamCount - 1; i >= 0; i--)
                {
                    temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                    il.Emit(OpCodes.Stloc, temps[i]);
                }

                il.Emit(OpCodes.Ldarg_0); // TranspiledContext

                for (int i = 0; i < wasmParamCount; i++)
                {
                    il.Emit(OpCodes.Ldloc, temps[i]);
                }

                il.Emit(OpCodes.Call, targetMethod);
            }
        }
    }
}
