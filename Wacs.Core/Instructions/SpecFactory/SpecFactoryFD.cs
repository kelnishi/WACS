using System.IO;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Instructions.Simd;
using Wacs.Core.Instructions.SIMD;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions
{
    public partial class SpecFactory
    {
        public static IInstruction? CreateInstruction(SimdCode opcode) => opcode switch
        {
            SimdCode.V128Const => new InstV128Const(),
            
            //UnOps
            SimdCode.I8x16Abs => NumericInst.I8x16Abs,
            SimdCode.I16x8Abs => NumericInst.I16x8Abs,
            SimdCode.I32x4Abs => NumericInst.I32x4Abs,
            SimdCode.I64x2Abs => NumericInst.I64x2Abs,
            SimdCode.F32x4Abs => NumericInst.F32x4Abs,
            SimdCode.F64x2Abs => NumericInst.F64x2Abs,

            SimdCode.I8x16Neg => NumericInst.I8x16Neg,
            SimdCode.I16x8Neg => NumericInst.I16x8Neg,
            SimdCode.I32x4Neg => NumericInst.I32x4Neg,
            SimdCode.I64x2Neg => NumericInst.I64x2Neg,
            SimdCode.F32x4Neg => NumericInst.F32x4Neg,
            SimdCode.F64x2Neg => NumericInst.F64x2Neg,

            SimdCode.F32x4Sqrt => NumericInst.F32x4Sqrt,
            SimdCode.F64x2Sqrt => NumericInst.F64x2Sqrt,

            SimdCode.F32x4Ceil => NumericInst.F32x4Ceil,
            SimdCode.F64x2Ceil => NumericInst.F64x2Ceil,
            SimdCode.F32x4Floor => NumericInst.F32x4Floor,
            SimdCode.F64x2Floor => NumericInst.F64x2Floor,
            SimdCode.F32x4Trunc => NumericInst.F32x4Trunc,
            SimdCode.F64x2Trunc => NumericInst.F64x2Trunc,
            SimdCode.F32x4Nearest => NumericInst.F32x4Nearest,
            SimdCode.F64x2Nearest => NumericInst.F64x2Nearest,
            
            //Memory
            SimdCode.V128Load        => new InstMemoryLoad(ValType.V128, BitWidth.V128),
            SimdCode.V128Store       => new InstMemoryStore(ValType.V128, BitWidth.V128),
            SimdCode.V128Load8x8S    => new InstMemoryLoadMxN(BitWidth.S8, 8),
            SimdCode.V128Load8x8U    => new InstMemoryLoadMxN(BitWidth.U8, 8),
            SimdCode.V128Load16x4S   => new InstMemoryLoadMxN(BitWidth.S16, 4),
            SimdCode.V128Load16x4U   => new InstMemoryLoadMxN(BitWidth.U16, 4),
            SimdCode.V128Load32x2S   => new InstMemoryLoadMxN(BitWidth.S32, 2),
            SimdCode.V128Load32x2U   => new InstMemoryLoadMxN(BitWidth.U32, 2),
            SimdCode.V128Load8Splat  => new InstMemoryLoadSplat(BitWidth.U8),
            SimdCode.V128Load16Splat => new InstMemoryLoadSplat(BitWidth.U16),
            SimdCode.V128Load32Splat => new InstMemoryLoadSplat(BitWidth.U32),
            SimdCode.V128Load64Splat => new InstMemoryLoadSplat(BitWidth.U64),
            
            SimdCode.V128Load8Lane => null,  
            SimdCode.V128Load16Lane => null, 
            SimdCode.V128Load32Lane => null, 
            SimdCode.V128Load64Lane => null, 
            SimdCode.V128Store8Lane => null, 
            SimdCode.V128Store16Lane => null,
            SimdCode.V128Store32Lane => null,
            SimdCode.V128Store64Lane => null,
            SimdCode.V128Load32Zero => null, 
            SimdCode.V128Load64Zero => null, 
            
            _ => throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFD{(byte)opcode:X2}")
        };
    }
}