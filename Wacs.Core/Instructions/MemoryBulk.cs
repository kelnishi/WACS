using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Utilities;

// 5.4.6 Memory Instructions
namespace Wacs.Core.Instructions
{
    //0x3F
    public class InstMemorySize : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemorySize;

        public byte MemoryIndex { get; private set; }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            MemoryIndex = reader.ReadByte();
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
        public byte MemoryIndex { get; private set; }
        
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            MemoryIndex = reader.ReadByte();
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
        public uint DataIndex { get; private set; }
        public byte MemoryIndex { get; private set; }
        
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            DataIndex = reader.ReadLeb128_u32();
            MemoryIndex = reader.ReadByte();
            
            if (MemoryIndex != 0x00)
                throw new InvalidDataException(
                    $"Invalid memory.init. Multiple memories are not yet supported. memidx:{MemoryIndex}");
            
            return this;
        }
    }
    
    //0xFC_09
    public class InstDataDrop : InstructionBase
    {
        public override OpCode OpCode => OpCode.DataDrop;
        public uint DataIndex { get; private set; }
        
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            DataIndex = reader.ReadLeb128_u32();
            return this;
        }
    }
    
    //0xFC_0A
    public class InstMemoryCopy : InstructionBase
    {
        public override OpCode OpCode => OpCode.MemoryCopy;
        public byte SrcMemoryIndex { get; private set; }
        public byte DstMemoryIndex { get; private set; }
        
        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            SrcMemoryIndex = reader.ReadByte();
            DstMemoryIndex = reader.ReadByte();

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