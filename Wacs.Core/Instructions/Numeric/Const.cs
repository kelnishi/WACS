using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Utilities;

namespace Wacs.Core.Instructions.Numeric
{
    //0x41
    public class InstI32Const : InstructionBase
    {
        public override OpCode OpCode => OpCode.I32Const;
        private int Value { get; set; }
        
        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushI32(Value);

        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.ReadLeb128_s32();
            return this;
        }
    }
    
    //0x42
    public class InstI64Const : InstructionBase
    {
        public override OpCode OpCode => OpCode.I64Const;
        private long Value { get; set; }
        
        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushI64(Value);
        
        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.ReadLeb128_s64();
            return this;
        }
    }
    
    //0x43
    public class InstF32Const : InstructionBase
    {
        public override OpCode OpCode => OpCode.F32Const;
        private float Value { get; set; }
        
        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushF32(Value);
        
        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.Read_f32();
            return this;
        }
    }
    
    //0x44
    public class InstF64Const : InstructionBase
    {
        public override OpCode OpCode => OpCode.F64Const;
        public double Value { get; internal set; }
        
        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushF64(Value);

        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.Read_f64();
            return this;
        }
    }
}