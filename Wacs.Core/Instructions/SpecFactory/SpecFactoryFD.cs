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
            SimdCode.V128Load32Zero  => new InstMemoryLoadZero(BitWidth.U32),
            SimdCode.V128Load64Zero  => new InstMemoryLoadZero(BitWidth.U64),
            SimdCode.V128Load8Lane   => new InstMemoryLoadLane(BitWidth.U8),  
            SimdCode.V128Load16Lane  => new InstMemoryLoadLane(BitWidth.U16), 
            SimdCode.V128Load32Lane  => new InstMemoryLoadLane(BitWidth.U32), 
            SimdCode.V128Load64Lane  => new InstMemoryLoadLane(BitWidth.U64), 
            SimdCode.V128Store8Lane  => new InstMemoryStoreLane(BitWidth.U8), 
            SimdCode.V128Store16Lane => new InstMemoryStoreLane(BitWidth.U16),
            SimdCode.V128Store32Lane => new InstMemoryStoreLane(BitWidth.U32),
            SimdCode.V128Store64Lane => new InstMemoryStoreLane(BitWidth.U64), 
            
            //VvUnOp
            SimdCode.V128Not     => NumericInst.V128Not,
             
            //VvBinOps 
            SimdCode.V128And     => NumericInst.V128And,
            SimdCode.V128AndNot  => NumericInst.V128AndNot,
            SimdCode.V128Or      => NumericInst.V128Or,
            SimdCode.V128Xor     => NumericInst.V128Xor,
            
            //ViUnOps
            SimdCode.I8x16Abs    => NumericInst.I8x16Abs,
            SimdCode.I16x8Abs    => NumericInst.I16x8Abs,
            SimdCode.I32x4Abs    => NumericInst.I32x4Abs,
            SimdCode.I64x2Abs    => NumericInst.I64x2Abs,
            SimdCode.I8x16Neg    => NumericInst.I8x16Neg,
            SimdCode.I16x8Neg    => NumericInst.I16x8Neg,
            SimdCode.I32x4Neg    => NumericInst.I32x4Neg,
            SimdCode.I64x2Neg    => NumericInst.I64x2Neg,
            SimdCode.I8x16Popcnt => NumericInst.I8x16Popcnt,
             
            //VvTernOp
            SimdCode.V128BitSelect => NumericInst.V128BitSelect,
            
            //VvTestOp
            SimdCode.V128AnyTrue   => NumericInst.V128AnyTrue,
            
            //ViTestOps
            SimdCode.I8x16AllTrue  => NumericInst.I8x16AllTrue,
            SimdCode.I16x8AllTrue  => NumericInst.I16x8AllTrue,
            SimdCode.I32x4AllTrue  => NumericInst.I32x4AllTrue,
            SimdCode.I64x2AllTrue  => NumericInst.I64x2AllTrue,

            //ViRelOps
            SimdCode.I8x16Eq       => NumericInst.I8x16Eq,
            SimdCode.I8x16Ne       => NumericInst.I8x16Ne,
            SimdCode.I8x16LtS      => NumericInst.I8x16LtS,
            SimdCode.I8x16LtU      => NumericInst.I8x16LtU,
            SimdCode.I8x16GtS      => NumericInst.I8x16GtS,
            SimdCode.I8x16GtU      => NumericInst.I8x16GtU,
            SimdCode.I8x16LeS      => NumericInst.I8x16LeS,
            SimdCode.I8x16LeU      => NumericInst.I8x16LeU,
            SimdCode.I8x16GeS      => NumericInst.I8x16GeS,
            SimdCode.I8x16GeU      => NumericInst.I8x16GeU,
            SimdCode.I16x8Eq       => NumericInst.I16x8Eq,
            SimdCode.I16x8Ne       => NumericInst.I16x8Ne,
            SimdCode.I16x8LtS      => NumericInst.I16x8LtS,
            SimdCode.I16x8LtU      => NumericInst.I16x8LtU,
            SimdCode.I16x8GtS      => NumericInst.I16x8GtS,
            SimdCode.I16x8GtU      => NumericInst.I16x8GtU,
            SimdCode.I16x8LeS      => NumericInst.I16x8LeS,
            SimdCode.I16x8LeU      => NumericInst.I16x8LeU,
            SimdCode.I16x8GeS      => NumericInst.I16x8GeS,
            SimdCode.I16x8GeU      => NumericInst.I16x8GeU,
            SimdCode.I32x4Eq       => NumericInst.I32x4Eq,
            SimdCode.I32x4Ne       => NumericInst.I32x4Ne,
            SimdCode.I32x4LtS      => NumericInst.I32x4LtS,
            SimdCode.I32x4LtU      => NumericInst.I32x4LtU,
            SimdCode.I32x4GtS      => NumericInst.I32x4GtS,
            SimdCode.I32x4GtU      => NumericInst.I32x4GtU,
            SimdCode.I32x4LeS      => NumericInst.I32x4LeS,
            SimdCode.I32x4LeU      => NumericInst.I32x4LeU,
            SimdCode.I32x4GeS      => NumericInst.I32x4GeS,
            SimdCode.I32x4GeU      => NumericInst.I32x4GeU,
            SimdCode.I64x2Eq       => NumericInst.I64x2Eq,
            SimdCode.I64x2Ne       => NumericInst.I64x2Ne,
            SimdCode.I64x2LtS      => NumericInst.I64x2LtS,
            SimdCode.I64x2GtS      => NumericInst.I64x2GtS,
            SimdCode.I64x2LeS      => NumericInst.I64x2LeS,
            SimdCode.I64x2GeS      => NumericInst.I64x2GeS,
            
            //VfRelOps
            SimdCode.F32x4Eq       => NumericInst.F32x4Eq,
            SimdCode.F32x4Ne       => NumericInst.F32x4Ne,
            SimdCode.F32x4Lt       => NumericInst.F32x4Lt,
            SimdCode.F32x4Gt       => NumericInst.F32x4Gt,
            SimdCode.F32x4Le       => NumericInst.F32x4Le,
            SimdCode.F32x4Ge       => NumericInst.F32x4Ge,
            SimdCode.F64x2Eq       => NumericInst.F64x2Eq,
            SimdCode.F64x2Ne       => NumericInst.F64x2Ne,
            SimdCode.F64x2Lt       => NumericInst.F64x2Lt,
            SimdCode.F64x2Gt       => NumericInst.F64x2Gt,
            SimdCode.F64x2Le       => NumericInst.F64x2Le,
            SimdCode.F64x2Ge       => NumericInst.F64x2Ge,
            
            //ViBinOps
            SimdCode.I8x16Add       => NumericInst.I8x16Add,
            SimdCode.I8x16Sub       => NumericInst.I8x16Sub,
            SimdCode.I8x16SubSatS  => NumericInst.I8x16SubSatS,
            SimdCode.I8x16SubSatU  => NumericInst.I8x16SubSatU,
            SimdCode.I16x8Add       => NumericInst.I16x8Add,
            SimdCode.I16x8Sub       => NumericInst.I16x8Sub,
            SimdCode.I16x8SubSatS   => NumericInst.I16x8SubSatS,
            SimdCode.I16x8SubSatU   => NumericInst.I16x8SubSatU,
            SimdCode.I32x4Add       => NumericInst.I32x4Add,
            SimdCode.I32x4Sub       => NumericInst.I32x4Sub,
            SimdCode.I64x2Add       => NumericInst.I64x2Add,
            SimdCode.I64x2Sub       => NumericInst.I64x2Sub,
            SimdCode.I16x8Mul       => NumericInst.I16x8Mul,
            SimdCode.I32x4Mul       => NumericInst.I32x4Mul,
            SimdCode.I64x2Mul       => NumericInst.I64x2Mul,
            
            SimdCode.I8x16AvgrU        => NumericInst.I8x16AvgrU,
            SimdCode.I16x8AvgrU        => NumericInst.I16x8AvgrU,
            
            SimdCode.I16x8ExtAddPairwiseI8x16S => NumericInst.I16x8ExtAddPairwiseI8x16S,
            SimdCode.I16x8ExtAddPairwiseI8x16U => NumericInst.I16x8ExtAddPairwiseI8x16U,
            SimdCode.I32x4ExtAddPairwiseI16x8S => NumericInst.I32x4ExtAddPairwiseI16x8S,
            SimdCode.I32x4ExtAddPairwiseI16x8U => NumericInst.I32x4ExtAddPairwiseI16x8U,
            
            SimdCode.I16x8ExtMulLowI8x16S  => NumericInst.I16x8ExtMulLowI8x16S,
            SimdCode.I16x8ExtMulHighI8x16S => NumericInst.I16x8ExtMulHighI8x16S,
            SimdCode.I16x8ExtMulLowI8x16U  => NumericInst.I16x8ExtMulLowI8x16U,
            SimdCode.I16x8ExtMulHighI8x16U => NumericInst.I16x8ExtMulHighI8x16U,
            SimdCode.I32x4ExtMulLowI16x8S  => NumericInst.I32x4ExtMulLowI16x8S,
            SimdCode.I32x4ExtMulHighI16x8S => NumericInst.I32x4ExtMulHighI16x8S,
            SimdCode.I32x4ExtMulLowI16x8U  => NumericInst.I32x4ExtMulLowI16x8U,
            SimdCode.I32x4ExtMulHighI16x8U => NumericInst.I32x4ExtMulHighI16x8U,
            SimdCode.I64x2ExtMulLowI32x4S  => NumericInst.I64x2ExtMulLowI32x4S,
            SimdCode.I64x2ExtMulHighI32x4S => NumericInst.I64x2ExtMulHighI32x4S,
            SimdCode.I64x2ExtMulLowI32x4U  => NumericInst.I64x2ExtMulLowI32x4U,
            SimdCode.I64x2ExtMulHighI32x4U => NumericInst.I64x2ExtMulHighI32x4U,
            
            SimdCode.I32x4DotI16x8S => NumericInst.I32x4DotI16x8S,
            SimdCode.I16x8Q15MulRSatS => NumericInst.I16x8Q15MulRSatS,
            
            //ViMinMaxOps
            SimdCode.I8x16MinS       => NumericInst.I8x16MinS,
            SimdCode.I8x16MaxS       => NumericInst.I8x16MaxS,
            SimdCode.I16x8MinS       => NumericInst.I16x8MinS,
            SimdCode.I16x8MaxS       => NumericInst.I16x8MaxS,
            SimdCode.I32x4MinS       => NumericInst.I32x4MinS,
            SimdCode.I32x4MaxS       => NumericInst.I32x4MaxS,
            SimdCode.I8x16MinU       => NumericInst.I8x16MinU,
            SimdCode.I8x16MaxU       => NumericInst.I8x16MaxU,
            SimdCode.I16x8MinU       => NumericInst.I16x8MinU,
            SimdCode.I16x8MaxU       => NumericInst.I16x8MaxU,
            SimdCode.I32x4MinU       => NumericInst.I32x4MinU,
            SimdCode.I32x4MaxU       => NumericInst.I32x4MaxU,
            
            //ViSatBinOps
            SimdCode.I8x16AddSatS  => NumericInst.I8x16AddSatS,
            SimdCode.I8x16AddSatU  => NumericInst.I8x16AddSatU,
            SimdCode.I16x8AddSatS   => NumericInst.I16x8AddSatS,
            SimdCode.I16x8AddSatU   => NumericInst.I16x8AddSatU,
            
            //ViShiftOps
            SimdCode.I8x16Shl       => NumericInst.I8x16Shl,
            SimdCode.I8x16ShrS      => NumericInst.I8x16ShrS,
            SimdCode.I8x16ShrU      => NumericInst.I8x16ShrU,
            SimdCode.I16x8Shl       => NumericInst.I16x8Shl,
            SimdCode.I16x8ShrS      => NumericInst.I16x8ShrS,
            SimdCode.I16x8ShrU      => NumericInst.I16x8ShrU,
            SimdCode.I32x4Shl       => NumericInst.I32x4Shl,
            SimdCode.I32x4ShrS      => NumericInst.I32x4ShrS,
            SimdCode.I32x4ShrU      => NumericInst.I32x4ShrU,
            SimdCode.I64x2Shl       => NumericInst.I64x2Shl,
            SimdCode.I64x2ShrS      => NumericInst.I64x2ShrS,
            SimdCode.I64x2ShrU      => NumericInst.I64x2ShrU,
            
            //VfUnOps
            SimdCode.F32x4Abs      => NumericInst.F32x4Abs,
            SimdCode.F64x2Abs      => NumericInst.F64x2Abs,
            SimdCode.F32x4Neg      => NumericInst.F32x4Neg,
            SimdCode.F64x2Neg      => NumericInst.F64x2Neg,
            SimdCode.F32x4Sqrt     => NumericInst.F32x4Sqrt,
            SimdCode.F64x2Sqrt     => NumericInst.F64x2Sqrt,
            SimdCode.F32x4Ceil     => NumericInst.F32x4Ceil,
            SimdCode.F64x2Ceil     => NumericInst.F64x2Ceil,
            SimdCode.F32x4Floor    => NumericInst.F32x4Floor,
            SimdCode.F64x2Floor    => NumericInst.F64x2Floor,
            SimdCode.F32x4Trunc    => NumericInst.F32x4Trunc,
            SimdCode.F64x2Trunc    => NumericInst.F64x2Trunc,
            SimdCode.F32x4Nearest  => NumericInst.F32x4Nearest,
            SimdCode.F64x2Nearest  => NumericInst.F64x2Nearest,
            
            //VfBinOps
            SimdCode.F32x4Add          => NumericInst.F32x4Add,
            SimdCode.F32x4Sub          => NumericInst.F32x4Sub,
            SimdCode.F32x4Mul          => NumericInst.F32x4Mul,
            SimdCode.F32x4Div          => NumericInst.F32x4Div,
            SimdCode.F32x4Min          => NumericInst.F32x4Min,
            SimdCode.F32x4Max          => NumericInst.F32x4Max,
            SimdCode.F32x4PMin         => NumericInst.F32x4PMin,
            SimdCode.F32x4PMax         => NumericInst.F32x4PMax,
            SimdCode.F64x2Add          => NumericInst.F64x2Add,
            SimdCode.F64x2Sub          => NumericInst.F64x2Sub,
            SimdCode.F64x2Mul          => NumericInst.F64x2Mul,
            SimdCode.F64x2Div          => NumericInst.F64x2Div,
            SimdCode.F64x2Min          => NumericInst.F64x2Min,
            SimdCode.F64x2Max          => NumericInst.F64x2Max,
            SimdCode.F64x2PMin         => NumericInst.F64x2PMin,
            SimdCode.F64x2PMax         => NumericInst.F64x2PMax,
            
            //ViInjectOps
            SimdCode.I8x16Splat       => NumericInst.I8x16Splat,
            SimdCode.I16x8Splat       => NumericInst.I16x8Splat,
            SimdCode.I32x4Splat       => NumericInst.I32x4Splat,
            SimdCode.I64x2Splat       => NumericInst.I64x2Splat,
            SimdCode.I8x16Bitmask     => NumericInst.I8x16Bitmask,
            SimdCode.I16x8Bitmask     => NumericInst.I16x8Bitmask,
            SimdCode.I32x4Bitmask     => NumericInst.I32x4Bitmask,
            SimdCode.I64x2Bitmask     => NumericInst.I64x2Bitmask,
            SimdCode.F32x4Splat       => NumericInst.F32x4Splat,
            SimdCode.F64x2Splat       => NumericInst.F64x2Splat,
            
            //ViReorderOps
            // SimdCode.I8x16Shuffle     => new InstShuffleOp(),
            // SimdCode.I8x16Swizzle     => NumericInst.I8x16Swizzle,
            
            //VfConvert
            SimdCode.F32x4ConvertI32x4S    => NumericInst.F32x4ConvertI32x4S,
            SimdCode.F32x4ConvertI32x4U    => NumericInst.F32x4ConvertI32x4U,
            SimdCode.F64x2ConvertLowI32x4S => NumericInst.F64x2ConvertLowI32x4S,
            SimdCode.F64x2ConvertLowI32x4U => NumericInst.F64x2ConvertLowI32x4U,
            SimdCode.F32x4DemoteF64x2Zero  => NumericInst.F32x4DemoteF64x2Zero, 
            SimdCode.F64x2PromoteLowF32x4  => NumericInst.F64x2PromoteLowF32x4, 
            
            //ViConvert
            SimdCode.I32x4TruncSatF32x4S     => NumericInst.I32x4TruncSatF32x4S,
            SimdCode.I32x4TruncSatF32x4U     => NumericInst.I32x4TruncSatF32x4U,
            SimdCode.I32x4TruncSatF64x2SZero => NumericInst.I32x4TruncSatF64x2SZero,
            SimdCode.I32x4TruncSatF64x2UZero => NumericInst.I32x4TruncSatF64x2UZero,
            SimdCode.I8x16NarrowI16x8S       => NumericInst.I8x16NarrowI16x8S,
            SimdCode.I8x16NarrowI16x8U       => NumericInst.I8x16NarrowI16x8U,
            SimdCode.I16x8NarrowI32x4S       => NumericInst.I16x8NarrowI32x4S,
            SimdCode.I16x8NarrowI32x4U       => NumericInst.I16x8NarrowI32x4U,
            SimdCode.I16x8ExtendLowI8x16S    => NumericInst.I16x8ExtendLowI8x16S,
            SimdCode.I16x8ExtendHighI8x16S   => NumericInst.I16x8ExtendHighI8x16S,
            SimdCode.I16x8ExtendLowI8x16U    => NumericInst.I16x8ExtendLowI8x16U,
            SimdCode.I16x8ExtendHighI8x16U   => NumericInst.I16x8ExtendHighI8x16U,
            SimdCode.I32x4ExtendLowI16x8S    => NumericInst.I32x4ExtendLowI16x8S,
            SimdCode.I32x4ExtendHighI16x8S   => NumericInst.I32x4ExtendHighI16x8S,
            SimdCode.I32x4ExtendLowI16x8U    => NumericInst.I32x4ExtendLowI16x8U,
            SimdCode.I32x4ExtendHighI16x8U   => NumericInst.I32x4ExtendHighI16x8U,
            SimdCode.I64x2ExtendLowI32x4S    => NumericInst.I64x2ExtendLowI32x4S,
            SimdCode.I64x2ExtendHighI32x4S   => NumericInst.I64x2ExtendHighI32x4S,
            SimdCode.I64x2ExtendLowI32x4U    => NumericInst.I64x2ExtendLowI32x4U,
            SimdCode.I64x2ExtendHighI32x4U   => NumericInst.I64x2ExtendHighI32x4U,
            
            //VLaneOps
            SimdCode.I8x16ExtractLaneS => InstLaneOp.I8x16ExtractLaneS,
            SimdCode.I8x16ExtractLaneU => InstLaneOp.I8x16ExtractLaneU,
            SimdCode.I8x16ReplaceLane  => InstLaneOp.I8x16ReplaceLane,
            SimdCode.I16x8ExtractLaneS => InstLaneOp.I16x8ExtractLaneS,
            SimdCode.I16x8ExtractLaneU => InstLaneOp.I16x8ExtractLaneU,
            SimdCode.I16x8ReplaceLane  => InstLaneOp.I16x8ReplaceLane,
            SimdCode.I32x4ExtractLane  => InstLaneOp.I32x4ExtractLane,
            SimdCode.I32x4ReplaceLane  => InstLaneOp.I32x4ReplaceLane,
            SimdCode.I64x2ExtractLane  => InstLaneOp.I64x2ExtractLane,
            SimdCode.I64x2ReplaceLane  => InstLaneOp.I64x2ReplaceLane,
            SimdCode.F32x4ExtractLane  => InstLaneOp.F32x4ExtractLane,
            SimdCode.F32x4ReplaceLane  => InstLaneOp.F32x4ReplaceLane,
            SimdCode.F64x2ExtractLane  => InstLaneOp.F64x2ExtractLane,
            SimdCode.F64x2ReplaceLane  => InstLaneOp.F64x2ReplaceLane,
            
            _ => throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFD{(byte)opcode:X2}")
        };
    }
}