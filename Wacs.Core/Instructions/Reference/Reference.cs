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
        public InstRefNull() : base(ByteCode.RefNull, +1) { }
        
        private ValType Type;

        // @Spec 3.3.2.1. ref.null t
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(Type.IsRefType(), $"Type was not a RefType:{Type}");
            context.OpStack.PushRef(Value.Null(Type));  // +1
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
        public InstRefIsNull() : base(ByteCode.RefIsNull) { }

        // @Spec 3.3.2.2. ref.is_null
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopRefType(); // -1
            context.OpStack.PushI32();    // +0  
        }

        // @Spec 4.4.2.2. ref.is_null
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsRefType,
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
        public InstRefFunc() : base(ByteCode.RefFunc, +1) { }
        
        public FuncIdx FunctionIndex { get; internal set; }
        
        // @Spec 3.3.2.3. ref.func x
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Funcs.Contains(FunctionIndex),
                "Instruction ref.func is invalid. (func {0}) was not in the context.",FunctionIndex);
            //Seems like C.Refs isn't strictly necessary since FunctionSpace collects all the references
            var func = context.Funcs[FunctionIndex];
            
            context.Assert(context.Types.Contains(func.TypeIndex),
                "Instruction ref.func is invalid. (type {0}) was not in the context.", func.TypeIndex);
            
            context.Assert(func.IsFullyDeclared(context),
                "Instruction ref.func is invalid. (func {0}) is not fully declared in the module.",FunctionIndex);
            var val = new Value(ValType.Ref | (ValType)func.TypeIndex);
            context.OpStack.PushFuncref(val);   // +1
        }

        // @Spec 4.4.2.3. ref.func x
        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame?.Module.FuncAddrs.Contains(FunctionIndex),
                $"Instruction ref.func failed. Could not find function address in the context");
            var a = context.Frame!.Module.FuncAddrs[FunctionIndex];
            var func = context.Store[a];
            var val = new Value(ValType.FuncRef, a.Value);
            //Increase type specificity
            if (func is FunctionInstance funcInst)
            {
                val.Type = ValType.Ref | (ValType)funcInst.DefType.DefIndex;
            }
            context.OpStack.PushRef(val);
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
        public InstRefEq() : base(ByteCode.RefEq, -1) { }
        
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopType(ValType.Eq);    // -1
            context.OpStack.PopType(ValType.Eq);    // -2
            context.OpStack.PushI32();              // -1
        }

        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsRefType,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value v2 = context.OpStack.PopRefType();
            context.Assert( context.OpStack.Peek().IsRefType,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value v1 = context.OpStack.PopRefType();

            int c = v1.RefEquals(v2, context.Frame.Module.Types) ? 1 : 0;
            context.OpStack.PushI32(c);
        }

        public static readonly InstRefEq Inst = new InstRefEq();
    }
    
    /// <summary>
    /// https://webassembly.github.io/gc/core/bikeshed/#-hrefsyntax-instr-refmathsfrefas_non_null①
    /// </summary>
    public class InstRefAsNonNull : InstructionBase
    {
        public InstRefAsNonNull() : base(ByteCode.RefAsNonNull) { }
        
        public override void Validate(IWasmValidationContext context)
        {
            var vRef = context.OpStack.PopRefType();    // -1
            context.OpStack.PushType(vRef.Type);              // +0
        }

        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsRefType,
                $"Instruction ref.is_null failed. Expected reftype on top of the stack.");
            Value vRef = context.OpStack.PopRefType();
            if (vRef.IsNullRef)
                throw new TrapException("Ref was null.");
            
            context.OpStack.PushRef(vRef);
        }

        public static readonly InstRefAsNonNull Inst = new InstRefAsNonNull();
    }
}