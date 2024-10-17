using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

// 5.4.6 Memory Instructions
namespace Wacs.Core.Instructions
{
    public class InstMemoryLoad : InstructionBase
    {
        public override OpCode OpCode => Type switch {
            ValType.I32 => Width switch {
                BitWidth.U32 => OpCode.I32Load,     //0x28
                BitWidth.S8 => OpCode.I32Load8S,    //0x2C
                BitWidth.U8 => OpCode.I32Load8U,    //0x2D
                BitWidth.S16 => OpCode.I32Load16S,  //0x2E
                BitWidth.U16 => OpCode.I32Load16U,  //0x2F
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {Width}")
            },
            ValType.I64 => Width switch {
                BitWidth.U64 => OpCode.I64Load,     //0x29
                BitWidth.S8 => OpCode.I64Load8S,    //0x30
                BitWidth.U8 => OpCode.I64Load8U,    //0x31
                BitWidth.S16 => OpCode.I64Load16S,  //0x32
                BitWidth.U16 => OpCode.I64Load16U,  //0x33
                BitWidth.S32 => OpCode.I64Load32S,  //0x34
                BitWidth.U32 => OpCode.I64Load32U,  //0x35
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {Width}")
            },
            ValType.F32 => OpCode.F32Load,          //0x2A
            ValType.F64 => OpCode.F64Load,          //0x2B
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type}"),
        };

        private ValType Type { get; }
        private BitWidth Width { get; }
        
        public MemArg Arg { get; internal set; }

        public InstMemoryLoad(ValType type, BitWidth width) => 
            (Type, Width) = (type, width);

        /// <summary>
        /// @Spec 3.3.7.1. t.load
        /// @Spec 3.3.7.2. t.loadN_sx
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                $"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(((2 << ((int)Arg.Align - 1)) <= Width.ByteSize()),
                $"Instruction {this.OpCode.GetMnemonic()} failed with invalid alignment 2^{Arg.Align} <= {Width}/8");
            
            context.OpStack.PopI32();
            context.OpStack.PushType(Type);
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            Arg = MemArg.Parse(reader);
            return this;
        }
    }
    
    public class InstMemoryStore : InstructionBase
    {
        public override OpCode OpCode => Type switch {
            ValType.I32 => Width switch {
                BitWidth.U8 => OpCode.I32Store8,    
                BitWidth.U16 => OpCode.I32Store16,
                BitWidth.U32 => OpCode.I32Store,
                _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type} {Width}")
            },
            ValType.I64 => Width switch {
                BitWidth.U8 => OpCode.I64Store8,
                BitWidth.U16 => OpCode.I64Store16,
                BitWidth.U32 => OpCode.I64Store32,
                BitWidth.U64 => OpCode.I64Store,
                _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type} {Width}")
            },
            ValType.F32 => OpCode.F32Store,
            ValType.F64 => OpCode.F64Store,
            _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type}"),
        };

        private ValType Type { get; }
        private BitWidth Width { get; }
        
        public MemArg Arg { get; internal set; }
        
        public InstMemoryStore(ValType type, BitWidth width) => 
            (Type, Width) = (type, width);

        /// <summary>
        /// @Spec 3.3.7.3. t.store
        /// @Spec 3.3.7.4. t.storeN
        /// </summary>
        public override void Validate(WasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0), 
                $"Instruction {this.OpCode.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(((2 << ((int)Arg.Align - 1)) <= Width.ByteSize()),
                $"Instruction {this.OpCode.GetMnemonic()} failed with invalid alignment 2^{Arg.Align} <= {Width}/8");
            
            context.OpStack.PopI32();
            context.OpStack.PopType(Type);
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }

        public override IInstruction Parse(BinaryReader reader) {
            Arg = MemArg.Parse(reader);
            return this;
        }
    }
    
    public struct MemArg
    {
        public uint Offset;
        public uint Align;

        public static MemArg Parse(BinaryReader reader) => new MemArg {
            Align = reader.ReadLeb128_u32(),
            Offset = reader.ReadLeb128_u32(),
        };
    }

    public enum BitWidth : sbyte
    {
        S8 = -8,
        S16 = -16,
        S32 = -32,
        
        U8 = 8,
        U16 = 16,
        U32 = 32,
        U64 = 64,
    }

    public static class BitWidthHelpers
    {
        public static int ByteSize(this BitWidth width) =>
            width switch
            {
                BitWidth.S8 => 1,
                BitWidth.S16 => 2,
                BitWidth.S32 => 4,
                BitWidth.U8 => 1,
                BitWidth.U16 => 2,
                BitWidth.U32 => 4,
                BitWidth.U64 => 8,
                var bits => (byte)bits/8 
            };
    }
                
}