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
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
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
        
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
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