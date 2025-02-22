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

using FluentValidation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.GC
{
    public class InstAnyConvertExtern : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => GcCode.AnyConvertExtern;
        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-externmathsfanyconvert_extern
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            Value actual = context.OpStack.PopAny();
            bool nullable = actual.Type.IsNullable();

            var expectedType = nullable ? ValType.ExternRef : ValType.Extern;
            context.Assert(actual.Type.Matches(expectedType,context.Types),
                "Instruction {0} was invalid. Wrong operand type {1} at top of stack.", Op.GetMnemonic(),actual.Type);

            var conversionType = nullable ? ValType.Any : ValType.AnyNN;
            context.OpStack.PushType(conversionType);
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-externmathsfanyconvert_extern①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            var refVal = context.OpStack.PopRefType();
            refVal.Type = refVal.GcRef switch
            {
                StoreStruct => refVal.Type.IsNullable() ? ValType.Struct : ValType.StructNN,
                StoreArray => refVal.Type.IsNullable() ? ValType.Array : ValType.ArrayNN,
                I31Ref => refVal.Type.IsNullable() ? ValType.I31 : ValType.I31NN,
                _ => refVal.Type.IsNullable() ? ValType.Any : ValType.Ref,
            };
            context.OpStack.PushValue(refVal);
        }
    }
    
    
    public class InstExternConvertAny : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => GcCode.AnyConvertExtern;
        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-externmathsfexternconvert_any
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            Value actual = context.OpStack.PopAny();
            bool nullable = actual.Type.IsNullable();

            var expectedType = nullable ? ValType.Any : ValType.AnyNN;
            context.Assert(actual.Type.Matches(expectedType,context.Types),
                "Instruction {0} was invalid. Wrong operand type {1} at top of stack.", Op.GetMnemonic(),actual.Type);
            
            var conversionType = nullable ? ValType.ExternRef : ValType.Extern;
            context.OpStack.PushType(conversionType);
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-externmathsfexternconvert_any①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            var refVal = context.OpStack.PopRefType();
            refVal.Type = refVal.Type.IsNullable() ? ValType.ExternRef : ValType.Extern;
            context.OpStack.PushValue(refVal);
        }
    }
}