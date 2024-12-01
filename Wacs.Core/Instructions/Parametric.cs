// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.3 Parametric Instructions
namespace Wacs.Core.Instructions
{
    //0x1A
    public class InstDrop : InstructionBase, INodeConsumer<Value>
    {
        public static readonly InstDrop Inst = new();
        public override ByteCode Op => OpCode.Drop;

        /// <summary>
        /// @Spec 3.3.4.1. drop
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            //* Value Polymorphic ignores type
            context.OpStack.PopAny();
        }

        /// <summary>
        /// @Spec 4.4.4.1. drop
        /// </summary>
        public override void Execute(ExecContext context)
        {
            context.OpStack.PopAny();
        }

        public Action<ExecContext, Value> GetFunc => (_, _) => { };
    }
    
    //0x1B
    public class InstSelect : InstructionBase, INodeComputer<Value, Value, int, Value>
    {
        public static readonly InstSelect InstWithoutTypes = new();

        private readonly bool WithTypes;
        private ValType[] Types = Array.Empty<ValType>();

        public InstSelect(bool withTypes = false) => WithTypes = withTypes;
        public override ByteCode Op => OpCode.Select;

        /// <summary>
        /// @Spec 3.3.4.2. select
        /// @Spec Appendix A.3 #validation-of-opcode-sequences①
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            if (WithTypes)
            {
                context.Assert(Types.Length == 1, "Select instruction type must be of length 1");
                var type = Types[0];
                context.OpStack.PopI32();
                context.OpStack.PopType(type);
                context.OpStack.PopType(type);
                context.OpStack.PushType(type);
            }
            else
            {
                context.OpStack.PopI32();
                Value val2 = context.OpStack.PopAny();
                Value val1 = context.OpStack.PopAny();
                context.Assert(val1.Type.IsCompatible(val2.Type),
                    "Select instruction expected matching types on the stack: {0} == {1}",val1.Type,val2.Type);

                if (!context.Attributes.Configure_RefTypes)
                {
                    switch (val1.Type)
                    {
                        case ValType.I32:
                        case ValType.I64:
                        case ValType.F32:
                        case ValType.F64:
                        case ValType.Unknown:
                            break;
                        default:
                            throw new ValidationException($"select does not support {val1.Type} in MVP");
                    }
                }
                
                context.OpStack.PushType(val1.Type == ValType.Unknown ? val2.Type : val1.Type);
            }
        }

        /// <summary>
        /// @Spec 4.4.4.2. select
        /// </summary>
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            Value val2 = context.OpStack.PopAny();
            Value val1 = context.OpStack.PopAny();
            context.OpStack.PushValue(Select(context, val1, val2, c));
        }

        private Value Select(ExecContext _, Value val1, Value val2, int c) => 
            c != 0 ? val1 : val2;

        public Func<ExecContext, Value, Value, int, Value> GetFunc => Select;
        public override IInstruction Parse(BinaryReader reader)
        {
            if (WithTypes) {
                Types = reader.ParseVector(ValTypeParser.Parse);
            }
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)}{(WithTypes ? $" {new ResultType(Types).ToResults()}" : "")}";
    }
    
}