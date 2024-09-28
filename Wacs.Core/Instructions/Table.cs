using System;
using System.IO;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;
using Wacs.Core.Utilities;


// 5.4.5 Table Instructions
namespace Wacs.Core.Instructions
{
    // 0x25
    public class InstTableGet : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableGet;
        public uint TableIndex { get; private set; }

        public override void Execute(ExecContext context)
        {
            // Fetch the element from the table and push it onto the stack
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = reader.ReadLeb128_u32();
            return this;
        }
    }

    // 0x26
    public class InstTableSet : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableSet;
        public uint TableIndex { get; private set; }

        public override void Execute(ExecContext context)
        {
            // Set the element in the table, potentially popping the value from the stack
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = reader.ReadLeb128_u32();
            return this;
        }
    }

    // 0xFC0C
    public class InstTableInit : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableInit;
        public uint TableIndex { get; internal set; }
        public uint ElementIndex { get; internal set; } // This may be updated depending on the value type specification

        public override void Execute(ExecContext context)
        {
            // Grow the table by the specified number of elements of the specified type
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            TableIndex = reader.ReadUInt32();
            ElementIndex = reader.ReadUInt32(); 
            return this;
        }
    }
    
    // 0xFC0F
    public class InstElemDrop : InstructionBase
    {
        public override OpCode OpCode => OpCode.ElemDrop;
        public uint ElementIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            ElementIndex = reader.ReadUInt32();
            return this;
        }
    }
    
    // 0xFC0E
    public class InstTableCopy : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableCopy;
        public uint SrcTableIndex { get; internal set; }
        public uint DstTableIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            SrcTableIndex = reader.ReadUInt32();
            DstTableIndex = reader.ReadUInt32();
            return this;
        }
    }
    
    // 0xFC0F
    public class InstTableGrow : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableGrow;
        public uint TableIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            TableIndex = reader.ReadUInt32();
            return this;
        }
    }
    // 0xFC10
    public class InstTableSize : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableSize;
        public uint TableIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            TableIndex = reader.ReadUInt32();
            return this;
        }
    }
    // 0xFC11
    public class InstTableFill : InstructionBase
    {
        public override OpCode OpCode => OpCode.TableFill;
        public uint TableIndex { get; internal set; }

        public override void Execute(ExecContext context) {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            TableIndex = reader.ReadUInt32();
            return this;
        }
    }
    
}
