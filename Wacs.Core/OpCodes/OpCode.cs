using Wacs.Core.Attributes;

// ReSharper disable InconsistentNaming

namespace Wacs.Core.OpCodes
{
    /// <summary>
    /// Represents all WebAssembly opcodes, including core instructions and those from extensions and proposals.
    /// see WebAssembly Specification Release 2.0 (Draft 2024-09-26)
    /// </summary>
    public enum OpCode : ushort
    {
        // =========================
        // Control Instructions
        // =========================

        [OpCode("unreachable")]   Unreachable  = 0x00,
        [OpCode("nop")]           Nop          = 0x01,
        [OpCode("block")]         Block        = 0x02,
        [OpCode("loop")]          Loop         = 0x03,
        [OpCode("if")]            If           = 0x04,
        [OpCode("else")]          Else         = 0x05,

        [OpCode("end")]           End          = 0x0B,
        [OpCode("br")]            Br           = 0x0C,
        [OpCode("br_if")]         BrIf         = 0x0D,
        [OpCode("br_table")]      BrTable      = 0x0E,
        [OpCode("return")]        Return       = 0x0F,
        [OpCode("call")]          Call         = 0x10,
        [OpCode("call_indirect")] CallIndirect = 0x11,
        
        // Exception Handling (Proposal)
        [OpCode("try")] Try = 0x06,
        [OpCode("catch")] Catch = 0x07,
        [OpCode("throw")] Throw = 0x08,
        [OpCode("rethrow")] Rethrow = 0x09,
        [OpCode("br_on_exn")] BrOnExn = 0x0A,
        
        // Tail Calls proposal
        [OpCode("return_call")] ReturnCall = 0x12,           
        [OpCode("return_call_indirect")] ReturnCallIndirect = 0x13,

        // Reference Types extension
        [OpCode("call_ref")] CallRef = 0x14,         
        
        // =========================
        // Reference Types
        // =========================

        [OpCode("ref.null")]    RefNull   = 0xD0,
        [OpCode("ref.is_null")] RefIsNull = 0xD1,
        [OpCode("ref.func")]    RefFunc   = 0xD2,

        // =========================
        // Parametric Instructions
        // =========================

        [OpCode("drop")]   Drop    = 0x1A,
        [OpCode("select")] Select  = 0x1B,
        [OpCode("select")] SelectT = 0x1C,              // With type, Reference Types extension

        // =========================
        // Variable Instructions
        // =========================

        [OpCode("local.get")]  LocalGet  = 0x20,
        [OpCode("local.set")]  LocalSet  = 0x21,
        [OpCode("local.tee")]  LocalTee  = 0x22,
        [OpCode("global.get")] GlobalGet = 0x23,
        [OpCode("global.set")] GlobalSet = 0x24,
        
        [OpCode("table.get")]  TableGet  = 0x25,
        [OpCode("table.set")]  TableSet  = 0x26,
        [OpCode("table.init")] TableInit = 0xFC0C,  //12
        [OpCode("elem.drop")]  ElemDrop  = 0xFC0D,  //13
        [OpCode("table.copy")] TableCopy = 0xFC0E,  //14
        [OpCode("table.grow")] TableGrow = 0xFC0F,  //15
        [OpCode("table.size")] TableSize = 0xFC10,  //16
        [OpCode("table.fill")] TableFill = 0xFC11,  //17

        // =========================
        // Memory Instructions
        // =========================

        [OpCode("i32.load")]     I32Load    = 0x28,
        [OpCode("i64.load")]     I64Load    = 0x29,
        [OpCode("f32.load")]     F32Load    = 0x2A,
        [OpCode("f64.load")]     F64Load    = 0x2B,
        [OpCode("i32.load8_s")]  I32Load8S  = 0x2C,
        [OpCode("i32.load8_u")]  I32Load8U  = 0x2D,
        [OpCode("i32.load16_s")] I32Load16S = 0x2E,
        [OpCode("i32.load16_u")] I32Load16U = 0x2F,
        [OpCode("i64.load8_s")]  I64Load8S  = 0x30,
        [OpCode("i64.load8_u")]  I64Load8U  = 0x31,
        [OpCode("i64.load16_s")] I64Load16S = 0x32,
        [OpCode("i64.load16_u")] I64Load16U = 0x33,
        [OpCode("i64.load32_s")] I64Load32S = 0x34,
        [OpCode("i64.load32_u")] I64Load32U = 0x35,
        [OpCode("i32.store")]    I32Store   = 0x36,
        [OpCode("i64.store")]    I64Store   = 0x37,
        [OpCode("f32.store")]    F32Store   = 0x38,
        [OpCode("f64.store")]    F64Store   = 0x39,
        [OpCode("i32.store8")]   I32Store8  = 0x3A,
        [OpCode("i32.store16")]  I32Store16 = 0x3B,
        [OpCode("i64.store8")]   I64Store8  = 0x3C,
        [OpCode("i64.store16")]  I64Store16 = 0x3D,
        [OpCode("i64.store32")]  I64Store32 = 0x3E,
        [OpCode("memory.size")]  MemorySize = 0x3F,
        [OpCode("memory.grow")]  MemoryGrow = 0x40,

        // =========================
        // Numeric Constants
        // =========================

        [OpCode("i32.const")] I32Const = 0x41,
        [OpCode("i64.const")] I64Const = 0x42,
        [OpCode("f32.const")] F32Const = 0x43,
        [OpCode("f64.const")] F64Const = 0x44,

        // =========================
        // Numeric Instructions
        // =========================

        // --- Integer Comparisons and Operations ---

        // I32 Integer Comparisons

        [OpCode("i32.eqz")]  I32Eqz = 0x45,
        [OpCode("i32.eq")]   I32Eq  = 0x46,
        [OpCode("i32.ne")]   I32Ne  = 0x47,
        [OpCode("i32.lt_s")] I32LtS = 0x48,
        [OpCode("i32.lt_u")] I32LtU = 0x49,
        [OpCode("i32.gt_s")] I32GtS = 0x4A,
        [OpCode("i32.gt_u")] I32GtU = 0x4B,
        [OpCode("i32.le_s")] I32LeS = 0x4C,
        [OpCode("i32.le_u")] I32LeU = 0x4D,
        [OpCode("i32.ge_s")] I32GeS = 0x4E,
        [OpCode("i32.ge_u")] I32GeU = 0x4F,

        // I64 Integer Comparisons

        [OpCode("i64.eqz")]  I64Eqz = 0x50,
        [OpCode("i64.eq")]   I64Eq  = 0x51,
        [OpCode("i64.ne")]   I64Ne  = 0x52,
        [OpCode("i64.lt_s")] I64LtS = 0x53,
        [OpCode("i64.lt_u")] I64LtU = 0x54,
        [OpCode("i64.gt_s")] I64GtS = 0x55,
        [OpCode("i64.gt_u")] I64GtU = 0x56,
        [OpCode("i64.le_s")] I64LeS = 0x57,
        [OpCode("i64.le_u")] I64LeU = 0x58,
        [OpCode("i64.ge_s")] I64GeS = 0x59,
        [OpCode("i64.ge_u")] I64GeU = 0x5A,

        // --- Floating-Point Comparisons ---

        // F32 Floating-Point Comparisons

        [OpCode("f32.eq")] F32Eq = 0x5B,
        [OpCode("f32.ne")] F32Ne = 0x5C,
        [OpCode("f32.lt")] F32Lt = 0x5D,
        [OpCode("f32.gt")] F32Gt = 0x5E,
        [OpCode("f32.le")] F32Le = 0x5F,
        [OpCode("f32.ge")] F32Ge = 0x60,

        // F64 Floating-Point Comparisons

        [OpCode("f64.eq")] F64Eq = 0x61,
        [OpCode("f64.ne")] F64Ne = 0x62,
        [OpCode("f64.lt")] F64Lt = 0x63,
        [OpCode("f64.gt")] F64Gt = 0x64,
        [OpCode("f64.le")] F64Le = 0x65,
        [OpCode("f64.ge")] F64Ge = 0x66,

        // --- Integer Operators ---

        // I32 Integer Operators

        [OpCode("i32.clz")]    I32Clz        = 0x67,
        [OpCode("i32.ctz")]    I32Ctz        = 0x68,
        [OpCode("i32.popcnt")] I32Popcnt     = 0x69,
        [OpCode("i32.add")]    I32Add        = 0x6A,
        [OpCode("i32.sub")]    I32Sub        = 0x6B,
        [OpCode("i32.mul")]    I32Mul        = 0x6C,
        [OpCode("i32.div_s")]  I32DivS       = 0x6D,
        [OpCode("i32.div_u")]  I32DivU       = 0x6E,
        [OpCode("i32.rem_s")]  I32RemS       = 0x6F,
        [OpCode("i32.rem_u")]  I32RemU       = 0x70,
        [OpCode("i32.and")]    I32And        = 0x71,
        [OpCode("i32.or")]     I32Or         = 0x72,
        [OpCode("i32.xor")]    I32Xor        = 0x73,
        [OpCode("i32.shl")]    I32Shl        = 0x74,
        [OpCode("i32.shr_s")]  I32ShrS       = 0x75,
        [OpCode("i32.shr_u")]  I32ShrU       = 0x76,
        [OpCode("i32.rotl")]   I32Rotl       = 0x77,
        [OpCode("i32.rotr")]   I32Rotr       = 0x78,

        // I64 Integer Operators

        [OpCode("i64.clz")]    I64Clz        = 0x79,
        [OpCode("i64.ctz")]    I64Ctz        = 0x7A,
        [OpCode("i64.popcnt")] I64Popcnt     = 0x7B,
        [OpCode("i64.add")]    I64Add        = 0x7C,
        [OpCode("i64.sub")]    I64Sub        = 0x7D,
        [OpCode("i64.mul")]    I64Mul        = 0x7E,
        [OpCode("i64.div_s")]  I64DivS       = 0x7F,
        [OpCode("i64.div_u")]  I64DivU       = 0x80,
        [OpCode("i64.rem_s")]  I64RemS       = 0x81,
        [OpCode("i64.rem_u")]  I64RemU       = 0x82,
        [OpCode("i64.and")]    I64And        = 0x83,
        [OpCode("i64.or")]     I64Or         = 0x84,
        [OpCode("i64.xor")]    I64Xor        = 0x85,
        [OpCode("i64.shl")]    I64Shl        = 0x86,
        [OpCode("i64.shr_s")]  I64ShrS       = 0x87,
        [OpCode("i64.shr_u")]  I64ShrU       = 0x88,
        [OpCode("i64.rotl")]   I64Rotl       = 0x89,
        [OpCode("i64.rotr")]   I64Rotr       = 0x8A,

        // --- Floating-Point Operators ---

        // F32 Floating-Point Operators
        [OpCode("f32.abs")]      F32Abs      = 0x8B,
        [OpCode("f32.neg")]      F32Neg      = 0x8C,
        [OpCode("f32.ceil")]     F32Ceil     = 0x8D,
        [OpCode("f32.floor")]    F32Floor    = 0x8E,
        [OpCode("f32.trunc")]    F32Trunc    = 0x8F,
        [OpCode("f32.nearest")]  F32Nearest  = 0x90,
        [OpCode("f32.sqrt")]     F32Sqrt     = 0x91,
        [OpCode("f32.add")]      F32Add      = 0x92,
        [OpCode("f32.sub")]      F32Sub      = 0x93,
        [OpCode("f32.mul")]      F32Mul      = 0x94,
        [OpCode("f32.div")]      F32Div      = 0x95,
        [OpCode("f32.min")]      F32Min      = 0x96,
        [OpCode("f32.max")]      F32Max      = 0x97,
        [OpCode("f32.copysign")] F32Copysign = 0x98,

        // F64 Floating-Point Operators
        [OpCode("f64.abs")]      F64Abs      = 0x99,
        [OpCode("f64.neg")]      F64Neg      = 0x9A,
        [OpCode("f64.ceil")]     F64Ceil     = 0x9B,
        [OpCode("f64.floor")]    F64Floor    = 0x9C,
        [OpCode("f64.trunc")]    F64Trunc    = 0x9D,
        [OpCode("f64.nearest")]  F64Nearest  = 0x9E,
        [OpCode("f64.sqrt")]     F64Sqrt     = 0x9F,
        [OpCode("f64.add")]      F64Add      = 0xA0,
        [OpCode("f64.sub")]      F64Sub      = 0xA1,
        [OpCode("f64.mul")]      F64Mul      = 0xA2,
        [OpCode("f64.div")]      F64Div      = 0xA3,
        [OpCode("f64.min")]      F64Min      = 0xA4,
        [OpCode("f64.max")]      F64Max      = 0xA5,
        [OpCode("f64.copysign")] F64Copysign = 0xA6,

        // --- Conversions ---
        [OpCode("i32.wrap_i64")]        I32WrapI64        = 0xA7,
        [OpCode("i32.trunc_f32_s")]     I32TruncF32S      = 0xA8,
        [OpCode("i32.trunc_f32_u")]     I32TruncF32U      = 0xA9,
        [OpCode("i32.trunc_f64_s")]     I32TruncF64S      = 0xAA,
        [OpCode("i32.trunc_f64_u")]     I32TruncF64U      = 0xAB,
        [OpCode("i64.extend_i32_s")]    I64ExtendI32S     = 0xAC,
        [OpCode("i64.extend_i32_u")]    I64ExtendI32U     = 0xAD,
        [OpCode("i64.trunc_f32_s")]     I64TruncF32S      = 0xAE,
        [OpCode("i64.trunc_f32_u")]     I64TruncF32U      = 0xAF,
        [OpCode("i64.trunc_f64_s")]     I64TruncF64S      = 0xB0,
        [OpCode("i64.trunc_f64_u")]     I64TruncF64U      = 0xB1,
        [OpCode("f32.convert_i32_s")]   F32ConvertI32S    = 0xB2,
        [OpCode("f32.convert_i32_u")]   F32ConvertI32U    = 0xB3,
        [OpCode("f32.convert_i64_s")]   F32ConvertI64S    = 0xB4,
        [OpCode("f32.convert_i64_u")]   F32ConvertI64U    = 0xB5,
        [OpCode("f32.demote_f64")]      F32DemoteF64      = 0xB6,
        [OpCode("f64.convert_i32_s")]   F64ConvertI32S    = 0xB7,
        [OpCode("f64.convert_i32_u")]   F64ConvertI32U    = 0xB8,
        [OpCode("f64.convert_i64_s")]   F64ConvertI64S    = 0xB9,
        [OpCode("f64.convert_i64_u")]   F64ConvertI64U    = 0xBA,
        [OpCode("f64.promote_f32")]     F64PromoteF32     = 0xBB,
        [OpCode("i32.reinterpret_f32")] I32ReinterpretF32 = 0xBC,
        [OpCode("i64.reinterpret_f64")] I64ReinterpretF64 = 0xBD,
        [OpCode("f32.reinterpret_i32")] F32ReinterpretI32 = 0xBE,
        [OpCode("f64.reinterpret_i64")] F64ReinterpretI64 = 0xBF,

        // =========================
        // Sign-Extension Operators
        // =========================

        [OpCode("i32.extend8_s")]  I32Extend8S  = 0xC0,
        [OpCode("i32.extend16_s")] I32Extend16S = 0xC1,
        [OpCode("i64.extend8_s")]  I64Extend8S  = 0xC2,
        [OpCode("i64.extend16_s")] I64Extend16S = 0xC3,
        [OpCode("i64.extend32_s")] I64Extend32S = 0xC4,

        // =========================
        // GC Proposal Operators
        // =========================
        
        // ReSharper disable once InconsistentNaming
        GCPrefix = 0xFB,
        
        // =========================
        // Non-Trapping Float-to-Int Conversions
        // =========================

        // Prefix: 0xFC
        ExtensionPrefix = 0xFC,
        [OpCode("i32.trunc_sat_f32_s")] I32TruncSatF32S = 0xFC00,
        [OpCode("i32.trunc_sat_f32_u")] I32TruncSatF32U = 0xFC01,
        [OpCode("i32.trunc_sat_f64_s")] I32TruncSatF64S = 0xFC02,
        [OpCode("i32.trunc_sat_f64_u")] I32TruncSatF64U = 0xFC03,
        [OpCode("i64.trunc_sat_f32_s")] I64TruncSatF32S = 0xFC04,
        [OpCode("i64.trunc_sat_f32_u")] I64TruncSatF32U = 0xFC05,
        [OpCode("i64.trunc_sat_f64_s")] I64TruncSatF64S = 0xFC06,
        [OpCode("i64.trunc_sat_f64_u")] I64TruncSatF64U = 0xFC07,

        // =========================
        // Bulk Memory Operations
        // =========================

        // Prefix: 0xFC
        [OpCode("memory.init")] MemoryInit = 0xFC08,
        [OpCode("data.drop")] DataDrop = 0xFC09,
        [OpCode("memory.copy")] MemoryCopy = 0xFC0A,
        [OpCode("memory.fill")] MemoryFill = 0xFC0B,

        // =========================
        // SIMD Extension (Proposal)
        // =========================

        // Note: SIMD opcodes have the prefix 0xFD and are numerous.
        // For brevity, only a few examples are included.

        // Prefix: 0xFD
        // ReSharper disable once InconsistentNaming
        SIMDPrefix = 0xFD,
        [OpCode("v128.load")] V128Load = 0xFD00,
        [OpCode("v128.store")] V128Store = 0xFD01,
        [OpCode("v128.const")] V128Const = 0xFD02,

        // ... (additional SIMD opcodes)

        // =========================
        // Atomic Instructions (Threads Extension)
        // =========================

        // Prefix: 0xFE
        ThreadsPrefix = 0xFE,
        [OpCode("atomic.notify")] AtomicNotify = 0xFE00,
        [OpCode("i32.atomic.wait")] I32AtomicWait = 0xFE01,
        [OpCode("i64.atomic.wait")] I64AtomicWait = 0xFE02,
        [OpCode("atomic.fence")] AtomicFence = 0xFE03,

        // ... (additional atomic opcodes)

        // =========================
        // Future Extensions
        // =========================

        // Placeholders for future opcodes or extensions can be added here.
    }
}