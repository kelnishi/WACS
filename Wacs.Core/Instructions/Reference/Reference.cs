using System;
using System.IO;
using System.Linq;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.2 Reference Instructions
namespace Wacs.Core.Instructions.Reference
{
    //0xD0
    public class InstRefNull : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.RefNull;
        private ValType Type;

        // @Spec 3.3.2.1. ref.null t
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(Type.IsRefType(), $"Type was not a RefType:{Type}");
            context.OpStack.PushRef(Value.Null(Type));
        }

        // @Spec 4.4.2.1. ref.null t
        public override void Execute(ExecContext context) {
            context.OpStack.PushRef(Value.Null(Type));
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            Type = ValTypeParser.ParseDefType(reader) | ValType.NullableRef;
            return this;
        }

        public InstructionBase Immediate(ValType type)
        {
            Type = type;
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
            context.OpStack.PushFuncref(new Value(ValType.Func, a.Value));
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            FunctionIndex = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }
        
        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {FunctionIndex.Value}";
    }

    /// <summary>
    /// https://webassembly.github.io/gc/core/bikeshed/#-hrefsyntax-instr-refmathsfrefeq①
    /// </summary>
    public class InstRefEq : InstructionBase
    {
        public override ByteCode Op => OpCode.RefEq;
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopType(ValType.Eq);
            context.OpStack.PopType(ValType.Eq);
            context.OpStack.PushI32();
        }

        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsRef,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value v2 = context.OpStack.PopRefType();
            context.Assert( context.OpStack.Peek().IsRef,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value v1 = context.OpStack.PopRefType();

            int c = v1.Equals(v2) ? 1 : 0;
            context.OpStack.PushI32(c);
        }

        public static readonly InstRefEq Inst = new InstRefEq();
    }
    
    /// <summary>
    /// https://webassembly.github.io/gc/core/bikeshed/#-hrefsyntax-instr-refmathsfrefas_non_null①
    /// </summary>
    public class InstRefAsNonNull : InstructionBase
    {
        public override ByteCode Op => OpCode.RefAsNonNull;
        public override void Validate(IWasmValidationContext context)
        {
            var vRef = context.OpStack.PopRefType();
            context.OpStack.PushType(vRef.Type);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsRef,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value vRef = context.OpStack.PopRefType();
            if (vRef.IsNullRef)
                throw new TrapException("Ref was null.");
            
            context.OpStack.PushRef(vRef);
        }

        public static readonly InstRefAsNonNull Inst = new InstRefAsNonNull();
    }
}