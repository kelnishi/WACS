using System;
using System.Collections.Generic;
using System.IO;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class Block
    {
        public BlockType Type { get; internal set; }

        private ValType ValType => Type switch {
            BlockType.I32 => ValType.I32,
            BlockType.I64 => ValType.I64,
            BlockType.F32 => ValType.F32,
            BlockType.F64 => ValType.F64,
            BlockType.V128 => ValType.V128,
            BlockType.Funcref => ValType.Funcref,
            BlockType.Externref => ValType.Externref,
            _ => ValType.Undefined
        };
        
        public bool IsEmpty => Type == BlockType.Empty;

        public TypeIdx TypeIndex => !Enum.IsDefined(typeof(BlockType), Type) ? (TypeIdx)(int)Type : (TypeIdx)0;

        public List<IInstruction> Instructions { get; set; } = new List<IInstruction>();

        private Block(long v) => Type = (BlockType)v;
        public Block(BlockType bt) => Type = bt;

        public static Block Empty = new Block(BlockType.Empty);
        
        public static Block Parse(BinaryReader reader) => new Block(reader.ReadLeb128_s33());
        
        /// <summary>
        /// @Spec 3.2.2. Block Types
        /// </summary>
        public class Validator : AbstractValidator<Block>
        {
            public Validator()
            {
                // @Spec 3.2.2.1. typeidx
                RuleFor(b => b.TypeIndex)
                    .Must((b, index, ctx) =>
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
        Empty = 0x40,
        
        // =========================
        // Numeric Types
        // =========================
        I32 = 0x7F,
        I64 = 0x7E,
        F32 = 0x7D,
        F64 = 0x7C,

        // =========================
        // Vector Types (SIMD)
        // =========================
        V128 = 0x7B, // (SIMD extension)

        // =========================
        // Reference Types
        // =========================
        Funcref = 0x70,
        Externref = 0x6F,
    }
}