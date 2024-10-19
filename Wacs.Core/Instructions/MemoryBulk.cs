using System;
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.6 Memory Instructions
namespace Wacs.Core.Instructions
{
    //0x3F
    public class InstMemorySize : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemorySize;

        public MemIdx MemoryIndex { get; private set; }

        /// <summary>
        /// @Spec 3.3.7.10. memory.size
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            context.OpStack.PushI32();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            MemoryIndex = (MemIdx)reader.ReadByte();
            if (MemoryIndex != 0x00)
                throw new InvalidDataException(
                    $"Invalid memory.size. Multiple memories are not yet supported. memidx:{MemoryIndex}");
            
            return this;
        }
    }
    
    //0x40
    public class InstMemoryGrow : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemoryGrow;
        public MemIdx MemoryIndex { get; private set; }

        /// <summary>
        /// @Spec 3.3.7.11. memory.grow
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            context.OpStack.PopI32();
            context.OpStack.PushI32();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            MemoryIndex = (MemIdx)reader.ReadByte();
            if (MemoryIndex != 0x00)
                throw new InvalidDataException(
                    $"Invalid memory.grow. Multiple memories are not yet supported. memidx:{MemoryIndex}");
            
            return this;
        }
    }
    
    //0xFC_08
    public class InstMemoryInit : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemoryInit;
        public DataIdx X { get; private set; }
        public MemIdx Index { get; private set; }

        /// <summary>
        /// @Spec 3.3.7.14. memory.init
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            
            context.Assert(context.Datas.Contains(X), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context data {X}.");

            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            X = (DataIdx)reader.ReadLeb128_u32();
            Index = (MemIdx)reader.ReadByte();
            
            if (Index != 0x00)
                throw new InvalidDataException(
                    $"Invalid memory.init. Multiple memories are not yet supported. memidx:{Index}");
            
            return this;
        }

        public IInstruction Immediate(DataIdx x)
        {
            X = x;
            return this;
        }
    }
    
    //0xFC_09
    public class InstDataDrop : InstructionBase
    {
        public override OpCode OpCode => OpCode.DataDrop;
        public DataIdx X { get; private set; }

        /// <summary>
        /// @Spec 3.3.7.15. data.drop
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Datas.Contains(X), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context data {X}.");
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            X = (DataIdx)reader.ReadLeb128_u32();
            return this;
        }

        public IInstruction Immediate(DataIdx x)
        {
            X = x;
            return this;
        }
    }
    
    //0xFC_0A
    public class InstMemoryCopy : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemoryCopy;
        public MemIdx SrcMemoryIndex { get; private set; }
        public MemIdx DstMemoryIndex { get; private set; }

        /// <summary>
        /// @Spec 3.3.7.13. memory.copy
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            SrcMemoryIndex = (MemIdx)reader.ReadByte();
            DstMemoryIndex = (MemIdx)reader.ReadByte();

            if (SrcMemoryIndex != 0x00 || DstMemoryIndex != 0x00)
            {
                throw new InvalidDataException($"Invalid memory.copy. Multiple memories are not yet supported. {SrcMemoryIndex} -> {DstMemoryIndex}");
            }
            
            return this;
        }
    }
    
    //0xFC_0B
    public class InstMemoryFill : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemoryFill;
        public byte MemoryIndex { get; private set; }

        /// <summary>
        /// @Spec 3.3.7.12. memory.fill
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                ()=>$"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            MemoryIndex = reader.ReadByte();
            if (MemoryIndex!= 0x00)
                throw new InvalidDataException($"Invalid memory.fill. Multiple memories are not yet supported. {MemoryIndex}");
            
            return this;
        }
    }
}