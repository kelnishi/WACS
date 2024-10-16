using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

// @Spec 2.4.8. Control Instructions
namespace Wacs.Core.Instructions
{
    //0x00
    public class InstUnreachable : InstructionBase
    {
        public override OpCode OpCode => OpCode.Unreachable;
        
        // @Spec 4.4.8.2. unreachable
        public override void Execute(IExecContext context)
        {
            throw new UnreachableException();
        }
        
        public class UnreachableException : Exception { }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) => this;
        
        public static readonly InstUnreachable Inst = new InstUnreachable();
    }
    
    //0x01
    public class InstNop : InstructionBase
    {
        public override OpCode OpCode => OpCode.Nop;
        
        // @Spec 4.4.8.1. nop
        public override void Execute(IExecContext context) { }
        
        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) => this;
        
        public static readonly InstNop Inst = new InstNop();
    }
    
    //0x02
    public class InstBlock : InstructionBase
    {
        public override OpCode OpCode => OpCode.Block;

        // @Spec 4.4.8.3. block
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }
        
        public Block Block { get; internal set; } = null!;

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Block = Block.Parse(reader);
            Block.Instructions = reader.ParseUntilNull(InstructionParser.Parse);
            return this;
        }
    }
    
    //0x03
    public class InstLoop : InstructionBase
    {
        public override OpCode OpCode => OpCode.Loop;

        // @Spec 4.4.8.4. loop
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }
        
        public Block Block { get; internal set; } = null!;

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            Block = Block.Parse(reader);
            Block.Instructions = reader.ParseUntilNull(InstructionParser.Parse);
            return this;
        }
    }
    
    //0x04
    public class InstIf : InstructionBase
    {
        public override OpCode OpCode => OpCode.If;

        // @Spec 4.4.8.5. if
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }

        public Block IfBlock { get; internal set; } = Block.Empty;
        public Block ElseBlock { get; internal set; } = Block.Empty;

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            IfBlock = Block.Parse(reader);
            var instructions= reader.ParseUntilNull(InstructionParser.Parse);
            int elseInst = instructions.FindIndex(inst=> inst is InstElse);
            if (elseInst == -1)
            {
                IfBlock.Instructions = instructions;
                ElseBlock.Instructions = new List<IInstruction>();
            }
            else
            {
                ElseBlock = new Block(IfBlock.Type);
                IfBlock.Instructions = instructions.GetRange(0, elseInst);
                int elseCount = instructions.Count - elseInst - 1; //Exclude the else instruction
                ElseBlock.Instructions = instructions.GetRange(elseInst, elseCount);
            }
            return this;
        }
    }

    //0x05
    public class InstElse : InstructionBase
    {
        public override OpCode OpCode => OpCode.Else;
        
        // @Spec 4.4.8.5. else
        public override void Execute(IExecContext context) { }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) => this;
        
        public static readonly InstElse Inst = new InstElse();
    }
    
    //0x0B
    // TODO We'll implement this when we need to render WAT.
    // public class InstEnd : InstructionBase
    // {
    //     public override OpCode OpCode => OpCode.End;
    //     public override void Execute(ExecContext context) { }
    //     public override IInstruction Parse(BinaryReader reader) => this;
    // }
    
    //0x0C
    public class InstBranch : InstructionBase
    {
        public override OpCode OpCode => OpCode.Br;
        
        public LabelIdx LabelIndex { get; internal set; }

        // @Spec 4.4.8.6. br
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) {
            LabelIndex = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x0D
    public class InstBranchConditional : InstructionBase
    {
        public override OpCode OpCode => OpCode.BrIf;
        
        public LabelIdx LabelIndex { get; internal set; }

        // @Spec 4.4.8.7. br_if
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) {
            LabelIndex = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x0E
    public class InstBranchTable : InstructionBase
    {
        public override OpCode OpCode => OpCode.BrTable;
        
        public LabelIdx[] LabelIndices { get; internal set; } = null!;
        public LabelIdx LabelIndex { get; internal set; }

        // @Spec 4.4.8.8. br_table
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }

        private static LabelIdx ParseLabelIndex(BinaryReader reader) =>
            (LabelIdx)reader.ReadLeb128_u32();
        
        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            LabelIndices = reader.ParseVector(ParseLabelIndex);
            LabelIndex = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x0F
    public class InstReturn : InstructionBase
    {
        public override OpCode OpCode => OpCode.Return;

        // @Spec 4.4.8.9. return
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader) => this;

        public static readonly InstReturn Inst = new InstReturn();
    }
    
    //0x10
    public class InstCall : InstructionBase
    {
        public override OpCode OpCode => OpCode.Call;
        
        public FuncIdx FunctionIndex { get; internal set; }

        // @Spec 4.4.8.10. call
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            FunctionIndex = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0x11
    public class InstCallIndirect  : InstructionBase
    {
        public override OpCode OpCode => OpCode.CallIndirect;
        
        public TypeIdx TypeIndex { get; internal set; }
        public TableIdx TableIndex { get; internal set; }

        // @Spec 4.4.8.11. call_indirect
        public override void Execute(IExecContext context)
        {
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override IInstruction Parse(BinaryReader reader)
        {
            TypeIndex = (TypeIdx)reader.ReadLeb128_u32();
            TableIndex = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
}