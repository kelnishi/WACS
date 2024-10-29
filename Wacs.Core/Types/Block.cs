using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class Block
    {
        public static readonly Block Empty = new(BlockType.Empty);

        public Block(BlockType type) => Type = type;
        public BlockType Type { get; }

        private ValType ValType => Type switch {
            BlockType.Empty => ValType.Nil,
            BlockType.Void => ValType.Nil,
            BlockType.I32 => ValType.I32,
            BlockType.I64 => ValType.I64,
            BlockType.F32 => ValType.F32,
            BlockType.F64 => ValType.F64,
            BlockType.V128 => ValType.V128,
            BlockType.Funcref => ValType.Funcref,
            BlockType.Externref => ValType.Externref,
            _ => ValType.Undefined
        };

        private TypeIdx TypeIndex => !Enum.IsDefined(typeof(BlockType), Type) ? (TypeIdx)(int)Type : (TypeIdx)uint.MaxValue;

        public InstructionSequence Instructions { get; set; } = new();

        public int Size => Instructions.Size;

        public bool IsEmpty => Type == BlockType.Empty;
        public static BlockType ParseBlockType(BinaryReader reader) => (BlockType)reader.ReadLeb128_s33();

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

    public enum BlockType : long
    {
        /// <summary>
        /// Block is empty
        /// </summary>
        Empty = -64, //0x40
        
        /// <summary>
        /// Block does not return any values
        /// </summary>
        Void = -1,
        
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