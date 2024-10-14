using System;
using System.IO;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

// 5.4.3 Parametric Instructions
namespace Wacs.Core.Instructions
{
    //0x1A
    public class InstDrop : InstructionBase
    {
        public override OpCode OpCode => OpCode.Drop;
        
        // @Spec 3.3.4.1. drop
        // @Spec 4.4.4.1. drop
        public override void Execute(ExecContext context)
        {
            StackValue value = context.OpStack.PopAny();
        }

        public override IInstruction Parse(BinaryReader reader) => this;

        public static readonly InstDrop Inst = new InstDrop();
    }
    
    //0x1B
    public class InstSelect : InstructionBase
    {
        public override OpCode OpCode => OpCode.Select;

        public bool WithTypes { get; internal set; } = false;
        public ValType[] Types { get; internal set; } = Array.Empty<ValType>();
        
        // @Spec 3.3.4.2. select
        // @Spec 4.4.4.2. select
        public override void Execute(ExecContext context)
        {
            int c = context.OpStack.PopI32();
            if (WithTypes)
            {
                if (Types.Length != 1)
                    throw new InvalidDataException($"Select instruction type must be of length 1");
                
                StackValue val2 = context.OpStack.PopAny();
                StackValue val1 = context.OpStack.PopAny();
                
                if (val1.Type != Types[0])
                    throw new InvalidProgramException(
                        $"Select instruction expected type on the stack: {val1.Type} == {Types[0]}");
                
                if (val2.Type != Types[0])
                    throw new InvalidProgramException(
                        $"Select instruction expected type on the stack: {val2.Type} == {Types[0]}");
                
                context.OpStack.PushValue(c != 0 ? val1 : val2);
            }
            else
            {
                StackValue val2 = context.OpStack.PopAny();
                StackValue val1 = context.OpStack.PopAny();
                if (val1.Type != val2.Type)
                    throw new InvalidProgramException(
                        $"Select instruction expected matching types on the stack: {val1.Type} == {val2.Type}");
                
                context.OpStack.PushValue(c != 0 ? val1 : val2);
            }
        }

        public InstSelect(bool withTypes = false) => WithTypes = withTypes;

        public override IInstruction Parse(BinaryReader reader)
        {
            if (WithTypes) {
                Types = reader.ParseVector(ValueTypeParser.Parse);
            }
            return this;
        }
        
        public static readonly InstSelect InstWithoutTypes = new InstSelect(false);
    }
    
}