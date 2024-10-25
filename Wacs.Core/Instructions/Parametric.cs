using System;
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.3 Parametric Instructions
namespace Wacs.Core.Instructions
{
    //0x1A
    public class InstDrop : InstructionBase
    {
        public static readonly InstDrop Inst = new();
        public override ByteCode Op => OpCode.Drop;

        /// <summary>
        /// @Spec 3.3.4.1. drop
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            //* Value Polymorphic ignores type
            context.OpStack.PopAny();
        }

        /// <summary>
        /// @Spec 4.4.4.1. drop
        /// </summary>
        public override void Execute(ExecContext context)
        {
            Value _ = context.OpStack.PopAny();
        }
    }
    
    //0x1B
    public class InstSelect : InstructionBase
    {
        public static readonly InstSelect InstWithoutTypes = new();

        public InstSelect(bool withTypes = false) => WithTypes = withTypes;
        public override ByteCode Op => OpCode.Select;

        private bool WithTypes { get; }
        private ValType[] Types { get; set; } = Array.Empty<ValType>();

        /// <summary>
        /// @Spec 3.3.4.2. select
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            if (WithTypes)
            {
                context.Assert(Types.Length == 1,
                    ()=>$"Select instruction type must be of length 1");
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
                context.Assert(val1.Type == val2.Type,
                    ()=>$"Select instruction expected matching types on the stack: {val1.Type} == {val2.Type}");
                context.OpStack.PushType(val1.Type);
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
            context.OpStack.PushValue(c != 0 ? val1 : val2);
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            if (WithTypes) {
                Types = reader.ParseVector(ValTypeParser.Parse);
            }
            return this;
        }
    }
    
}