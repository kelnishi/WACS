using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
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
        public override void Validate(WasmValidationContext context)
        {
             context.OpStack.PushType(Type.StackType());
        }

        // @Spec 4.4.2.1. ref.null t
        public override void Execute(ExecContext context) {
            context.OpStack.PushRef(Value.RefNull(Type));
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
        public override void Validate(WasmValidationContext context)
        {
            context.OpStack.PopRefType();
            context.OpStack.PushI32();
        }

        // @Spec 4.4.2.2. ref.is_null
        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsRef,
                ()=>$"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value val = context.OpStack.PopRefType();
            int booleanResult = val.IsNullRef ? 1 : 0;
            context.OpStack.PushI32(booleanResult);
        }

        public static readonly InstRefIsNull Inst = new InstRefIsNull();
    }
        
    
    //0xD2
    public class InstRefFunc : InstructionBase
    {
        public override OpCode OpCode => OpCode.RefFunc;
        
        public FuncIdx FunctionIndex { get; internal set; }
        
        // @Spec 3.3.2.3. ref.func x
        public override void Validate(WasmValidationContext context)
        { 
            context.Assert(context.Funcs.Contains(FunctionIndex),
                ()=>$"Instruction ref.func is invalid. Function {FunctionIndex} was not in the context.");
            //Seems like C.Refs isn't strictly necessary since FunctionSpace collects all the references
            var func = context.Funcs[FunctionIndex];
            context.OpStack.PushFuncref(Value.NullFuncRef);
        }

        // @Spec 4.4.2.3. ref.func x
        public override void Execute(ExecContext context)
        {
            context.Assert(context.Frame.Module.FuncAddrs.Contains(FunctionIndex),
                ()=>$"Instruction ref.func failed. Could not find function address in the context");
            var a = context.Frame.Module.FuncAddrs[FunctionIndex];
            context.OpStack.PushFuncref(new Value(ValType.Funcref, a.Value));
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            FunctionIndex = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
}