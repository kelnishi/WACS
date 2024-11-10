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
            SimdCode.V128Load32Zero => new InstMemoryLoadZero(BitWidth.U32),
            SimdCode.V128Load64Zero => new InstMemoryLoadZero(BitWidth.U64),
            SimdCode.V128Load8Lane  => new InstMemoryLoadLane(BitWidth.U8),  
            SimdCode.V128Load16Lane => new InstMemoryLoadLane(BitWidth.U16), 
            SimdCode.V128Load32Lane => new InstMemoryLoadLane(BitWidth.U32), 
            SimdCode.V128Load64Lane => new InstMemoryLoadLane(BitWidth.U64), 
            SimdCode.V128Store8Lane  => new InstMemoryStoreLane(BitWidth.U8), 
            SimdCode.V128Store16Lane => new InstMemoryStoreLane(BitWidth.U16),
            SimdCode.V128Store32Lane => new InstMemoryStoreLane(BitWidth.U32),
            SimdCode.V128Store64Lane => new InstMemoryStoreLane(BitWidth.U64), 
            
            //VvUnOp
            SimdCode.V128Not    => NumericInst.V128Not,
            
            //VvBinOps
            SimdCode.V128And    => NumericInst.V128And,
            SimdCode.V128AndNot => NumericInst.V128AndNot,
            SimdCode.V128Or     => NumericInst.V128Or,
            SimdCode.V128Xor    => NumericInst.V128Xor,
            
            //ViUnOps
            SimdCode.I8x16Abs => NumericInst.I8x16Abs,
            SimdCode.I16x8Abs => NumericInst.I16x8Abs,
            SimdCode.I32x4Abs => NumericInst.I32x4Abs,
            SimdCode.I64x2Abs => NumericInst.I64x2Abs,
            SimdCode.I8x16Neg => NumericInst.I8x16Neg,
            SimdCode.I16x8Neg => NumericInst.I16x8Neg,
            SimdCode.I32x4Neg => NumericInst.I32x4Neg,
            SimdCode.I64x2Neg => NumericInst.I64x2Neg,
            
            //VfUnOps
            SimdCode.F32x4Abs => NumericInst.F32x4Abs,
            SimdCode.F64x2Abs => NumericInst.F64x2Abs,
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
            
            //VvTernOp
            SimdCode.V128BitSelect => NumericInst.V128BitSelect,
            
            //VvTestOp
            SimdCode.V128AnyTrue => NumericInst.V128AnyTrue,
            
            //ViTestOps
            SimdCode.I8x16AllTrue => NumericInst.I8x16AllTrue,
            SimdCode.I16x8AllTrue => NumericInst.I16x8AllTrue,
            SimdCode.I32x4AllTrue => NumericInst.I32x4AllTrue,
            SimdCode.I64x2AllTrue => NumericInst.I64x2AllTrue,

            //ViRelOps
            SimdCode.I8x16Eq  => NumericInst.I8x16Eq,
            SimdCode.I8x16Ne  => NumericInst.I8x16Ne,
            SimdCode.I8x16LtS  => NumericInst.I8x16LtS,
            SimdCode.I8x16LtU  => NumericInst.I8x16LtU,
            SimdCode.I8x16GtS  => NumericInst.I8x16GtS,
            SimdCode.I8x16GtU  => NumericInst.I8x16GtU,
            SimdCode.I8x16LeS  => NumericInst.I8x16LeS,
            SimdCode.I8x16LeU  => NumericInst.I8x16LeU,
            SimdCode.I8x16GeS  => NumericInst.I8x16GeS,
            SimdCode.I8x16GeU  => NumericInst.I8x16GeU,
            SimdCode.I16x8Eq  => NumericInst.I16x8Eq,
            SimdCode.I16x8Ne  => NumericInst.I16x8Ne,
            SimdCode.I16x8LtS  => NumericInst.I16x8LtS,
            SimdCode.I16x8LtU  => NumericInst.I16x8LtU,
            SimdCode.I16x8GtS  => NumericInst.I16x8GtS,
            SimdCode.I16x8GtU  => NumericInst.I16x8GtU,
            SimdCode.I16x8LeS  => NumericInst.I16x8LeS,
            SimdCode.I16x8LeU  => NumericInst.I16x8LeU,
            SimdCode.I16x8GeS  => NumericInst.I16x8GeS,
            SimdCode.I16x8GeU  => NumericInst.I16x8GeU,
            SimdCode.I32x4Eq  => NumericInst.I32x4Eq,
            SimdCode.I32x4Ne  => NumericInst.I32x4Ne,
            SimdCode.I32x4LtS  => NumericInst.I32x4LtS,
            SimdCode.I32x4LtU  => NumericInst.I32x4LtU,
            SimdCode.I32x4GtS  => NumericInst.I32x4GtS,
            SimdCode.I32x4GtU  => NumericInst.I32x4GtU,
            SimdCode.I32x4LeS  => NumericInst.I32x4LeS,
            SimdCode.I32x4LeU  => NumericInst.I32x4LeU,
            SimdCode.I32x4GeS  => NumericInst.I32x4GeS,
            SimdCode.I32x4GeU  => NumericInst.I32x4GeU,
            SimdCode.I64x2Eq  => NumericInst.I64x2Eq,
            SimdCode.I64x2Ne  => NumericInst.I64x2Ne,
            SimdCode.I64x2LtS  => NumericInst.I64x2LtS,
            SimdCode.I64x2GtS  => NumericInst.I64x2GtS,
            SimdCode.I64x2LeS  => NumericInst.I64x2LeS,
            SimdCode.I64x2GeS  => NumericInst.I64x2GeS,
            
            
            
            _ => throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFD{(byte)opcode:X2}")
        };
    }
}