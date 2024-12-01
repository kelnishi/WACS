using System;
using System.IO;
using System.Linq;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.2 Reference Instructions
namespace Wacs.Core.Instructions
{
    //0xD0
    public class InstRefNull : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.RefNull;
        public ReferenceType Type { get; internal set; }

        // @Spec 3.3.2.1. ref.null t
        public override void Validate(IWasmValidationContext context)
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
        
        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {((HeapType)Type).ToWat()}";
    }
    
    //0xD1
    public class InstRefIsNull  : InstructionBase
    {
        public override ByteCode Op => OpCode.RefIsNull;

        // @Spec 3.3.2.2. ref.is_null
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopRefType();
            context.OpStack.PushI32();
        }

        // @Spec 4.4.2.2. ref.is_null
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsRef,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value val = context.OpStack.PopRefType();
            int booleanResult = val.IsNullRef ? 1 : 0;
            context.OpStack.PushI32(booleanResult);
        }

        public static readonly InstRefIsNull Inst = new InstRefIsNull();
    }
        
    
    //0xD2
    public class InstRefFunc : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.RefFunc;
        public FuncIdx FunctionIndex { get; internal set; }
        
        // @Spec 3.3.2.3. ref.func x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Funcs.Contains(FunctionIndex),
                "Instruction ref.func is invalid. Function {0} was not in the context.",FunctionIndex);
            //Seems like C.Refs isn't strictly necessary since FunctionSpace collects all the references
            var func = context.Funcs[FunctionIndex];
            
            context.Assert(func.IsFullyDeclared,
                "Instruction ref.func is invalid. Function {0} is not fully declared in the module.",FunctionIndex);
            
            context.OpStack.PushFuncref(Value.NullFuncRef);
        }

        // @Spec 4.4.2.3. ref.func x
        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame?.Module.FuncAddrs.Contains(FunctionIndex),
                $"Instruction ref.func failed. Could not find function address in the context");
            var a = context.Frame!.Module.FuncAddrs[FunctionIndex];
            context.OpStack.PushFuncref(new Value(ValType.Funcref, a.Value));
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            FunctionIndex = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }
        
        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {FunctionIndex.Value}";
    }
    
}