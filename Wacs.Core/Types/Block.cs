using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class Block
    {
        public static readonly Block Empty = new(BlockType.Empty) { Instructions = InstructionSequence.Empty };

        public Block(BlockType type) => Type = type;
        public BlockType Type { get; }

        private ValType ValType => Type switch {
            BlockType.Empty => ValType.Nil,
            BlockType.I32 => ValType.I32,
            BlockType.I64 => ValType.I64,
            BlockType.F32 => ValType.F32,
            BlockType.F64 => ValType.F64,
            BlockType.V128 => ValType.V128,
            BlockType.Funcref => ValType.Funcref,
            BlockType.Externref => ValType.Externref,
            _ => ValType.Undefined
        };

        private TypeIdx TypeIndex => !Enum.IsDefined(typeof(BlockType), Type) ? (TypeIdx)(uint)Type : (TypeIdx)uint.MaxValue;

        public InstructionSequence Instructions { get; set; } = new();


        /// <summary>
        /// The number of immediate child instructions 
        /// </summary>
        public int Length => Instructions.Length;

        /// <summary>
        /// The total number of instructions in the tree below
        /// </summary>
        public int Size => Instructions.Size;

        public bool IsEmptyType => Type == BlockType.Empty;

        public static BlockType ParseBlockType(BinaryReader reader)
        {
            byte byteValue = reader.ReadByte();
            if (Enum.IsDefined(typeof(BlockType), (uint)byteValue))
                return (BlockType)byteValue;
            
            //Continue parsing as LEB128_S33
            long result = 0;
            int shift = 0;
            bool moreBytes = true;

            while (moreBytes)
            {
                // Extract the lower 7 bits and add them to the result
                byte lower7Bits = (byte)(byteValue & 0x7F);
                result |= (long)lower7Bits << shift;

                // Increment the shift for the next 7 bits
                shift += 7;

                // Check if this is the last byte
                moreBytes = (byteValue & 0x80) != 0;

                // If it's the last byte, check the sign bit and perform sign extension if necessary
                if (!moreBytes)
                {
                    // If the sign bit of the last byte is set and shift is less than 33, sign-extend the result
                    if ((byteValue & 0x40) != 0 && shift < 33)
                    {
                        result |= -1L << shift;
                    }

                    break;
                }

                // Prevent shift overflow
                if (shift >= 64)
                    throw new FormatException("Shift count exceeds 64 bits while decoding s33.");

                byteValue = reader.ReadByte();
                if (byteValue == 0xFF)
                    throw new FormatException("Unexpected end of stream while decoding s33.");
            }

            if (result < 0)
                throw new FormatException($"BlockType Index {result} was negative");
            
            //Just take the U32 bits since the unset sign bit is 33.
            uint data = (uint)(result & 0xFFFF_FFFF);

            return (BlockType)data;
        }


        /// <summary>
        /// @Spec 3.2.2. Block Types
        /// </summary>
        public class Validator : AbstractValidator<Block>
        {
            public Validator()
            {
                // @Spec 3.2.2.1. typeidx
                RuleFor(b => b.TypeIndex)
                    .Must((_, index, ctx) =>
                        ctx.GetValidationContext().Types.Contains(index))
                    .When(b => b.ValType == ValType.Undefined)
                    .WithMessage("Blocks must have a valid typeidx referenced in Types");

                // @Spec 3.2.2.2. [valtype?]
                RuleFor(b => b.Type)
                    .IsInEnum()
                    .When(b => b.ValType != ValType.Undefined)
                    .WithMessage("Blocks must have a defined BlockType if not a ValType index");

            }
        }
    }

    public enum BlockType : uint
    {
        /// <summary>
        /// Block is empty
        /// </summary>
        Empty = 0x40, //0x40
        
        // =========================
        // Numeric Types returned
        // =========================
        I32 = 0x7F,
        I64 = 0x7E,
        F32 = 0x7D,
        F64 = 0x7C,

        // =========================
        // Vector Types (SIMD) returned
        // =========================
        V128 = 0x7B, // (SIMD extension)

        // =========================
        // Reference Types returned
        // =========================
        Funcref = 0x70,
        Externref = 0x6F,
    }
}