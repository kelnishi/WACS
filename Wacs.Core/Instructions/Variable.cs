using System;
using System.IO;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;
using Wacs.Core.Utilities;

// 5.4.4 Variable Instructions
namespace Wacs.Core.Instructions
{
    //0x20
    public class InstLocalGet : InstructionBase
    {
        public override OpCode OpCode => OpCode.LocalGet;
        public uint LocalIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            LocalIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x21
    public class InstLocalSet : InstructionBase
    {
        public override OpCode OpCode => OpCode.LocalSet;
        public uint LocalIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            LocalIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x22
    public class InstLocalTee : InstructionBase
    {
        public override OpCode OpCode => OpCode.LocalTee;
        public uint LocalIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            LocalIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x23
    public class InstGlobalGet : InstructionBase
    {
        public override OpCode OpCode => OpCode.GlobalGet;
        public uint GlobalIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            GlobalIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x24
    public class InstGlobalSet : InstructionBase
    {
        public override OpCode OpCode => OpCode.GlobalSet;
        public uint GlobalIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            GlobalIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    
}