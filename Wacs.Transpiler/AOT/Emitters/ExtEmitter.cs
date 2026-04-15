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

using System;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for 0xFC prefix WebAssembly instructions.
    ///
    /// Currently handles:
    /// - Saturating truncation (trunc_sat): non-trapping float-to-int conversions
    ///   that clamp to min/max instead of trapping on out-of-range values.
    ///
    /// Deferred: bulk memory (memory.copy/fill/init, data.drop),
    /// bulk table (table.init/copy, elem.drop).
    /// </summary>
    internal static class ExtEmitter
    {
        public static bool CanEmit(ExtCode op)
        {
            byte b = (byte)op;
            // Saturating truncation: 0x00-0x07
            if (b <= 0x07) return true;
            // Bulk memory/table: 0x08-0x0E
            if (BulkEmitter.CanEmit(op)) return true;
            // Table size/grow/fill: 0x0F-0x11
            if (TableRefEmitter.CanEmitExt(op)) return true;
            return false;
        }

        public static void Emit(ILGenerator il, InstructionBase inst, ExtCode op)
        {
            switch (op)
            {
                case ExtCode.I32TruncSatF32S:
                    EmitHelperCall(il, nameof(NumericInst.TruncSatF32S));
                    break;
                case ExtCode.I32TruncSatF32U:
                    EmitHelperCall(il, nameof(NumericInst.TruncSatF32U));
                    break;
                case ExtCode.I32TruncSatF64S:
                    EmitHelperCall(il, nameof(NumericInst.TruncSatF64S));
                    break;
                case ExtCode.I32TruncSatF64U:
                    EmitHelperCall(il, nameof(NumericInst.TruncSatF64U));
                    break;
                case ExtCode.I64TruncSatF32S:
                    EmitHelperCall(il, "TruncSatF32SToI64");
                    break;
                case ExtCode.I64TruncSatF32U:
                    EmitHelperCall(il, "TruncSatF32UToI64");
                    break;
                case ExtCode.I64TruncSatF64S:
                    EmitHelperCall(il, "TruncSatF64SToI64");
                    break;
                case ExtCode.I64TruncSatF64U:
                    EmitHelperCall(il, "TruncSatF64UToI64");
                    break;
                default:
                    if (BulkEmitter.CanEmit(op))
                    {
                        BulkEmitter.Emit(il, inst, op);
                        break;
                    }
                    if (TableRefEmitter.CanEmitExt(op))
                    {
                        TableRefEmitter.EmitExt(il, inst, op);
                        break;
                    }
                    throw new TranspilerException($"ExtEmitter: unhandled opcode {op}");
            }
        }

        private static void EmitHelperCall(ILGenerator il, string methodName)
        {
            var method = typeof(NumericInst).GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                throw new TranspilerException($"ExtEmitter: helper method {methodName} not found on NumericInst");
            il.Emit(OpCodes.Call, method);
        }
    }
}
