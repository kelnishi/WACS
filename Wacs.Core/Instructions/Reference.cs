using System;
using System.IO;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

// 5.4.2 Reference Instructions
namespace Wacs.Core.Instructions
{
    //0xD0
    public class InstRefNull : InstructionBase
    {
        public override OpCode OpCode => OpCode.RefNull;
        
        public ReferenceType Type { get; internal set; }

        // @Spec 3.3.2.1. ref.null t
        // @Spec 4.4.2.1. ref.null t
        public override void Execute(ExecContext context) {
            switch (Type) {
                case ReferenceType.Funcref:
                    context.Stack.PushFuncref(StackValue.NullFuncRef);
                    break;
                case ReferenceType.Externref:
                    context.Stack.PushExternref(StackValue.NullExternRef);
                    break;
                default:
                    throw new InvalidDataException($"Instruction ref.null had invalid type {Type}"); 
            }
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            Type = ReferenceTypeParser.Parse(reader);
            return this;
        }
    }
    
    //0xD1
    public class InstRefIsNull  : InstructionBase
    {
        public override OpCode OpCode => OpCode.RefIsNull;

        // @Spec 3.3.2.2. ref.is_null
        // @Spec 4.4.2.2. ref.is_null
        public override void Execute(ExecContext context)
        {
            StackValue value = context.Stack.PopRefType();
            int booleanResult = value.IsNullRef ? 1 : 0;
            context.Stack.PushI32(booleanResult);
        }

        public override IInstruction Parse(BinaryReader reader) => this;

        public static readonly InstRefIsNull Inst = new InstRefIsNull();
    }
        
    
    //0xD2
    public class InstRefFunc : InstructionBase
    {
        public override OpCode OpCode => OpCode.RefFunc;
        
        public UInt32 FunctionIndex { get; internal set; }
        
        
        // @Spec 3.3.2.3. ref.func x
        // @Spec 4.4.2.3. ref.func x
        public override void Execute(ExecContext context)
        {
            context.ValidateContext((ctx) => {
                if (!(FunctionIndex < ctx.Refs.Count))
                    throw new InvalidDataException($"Function index {FunctionIndex} is not defined in the context");
            });
            
            context.Stack.PushFuncref(new StackValue(ValType.Funcref, FunctionIndex));
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            FunctionIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
}