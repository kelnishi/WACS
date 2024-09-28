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

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
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

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) => this;

        public static readonly InstRefIsNull Inst = new InstRefIsNull();
    }
        
    
    //0xD2
    public class InstRefFunc : InstructionBase
    {
        public override OpCode OpCode => OpCode.RefFunc;
        
        public UInt32 FunctionIndex { get; internal set; }
        
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            FunctionIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
}