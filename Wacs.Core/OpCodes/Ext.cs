using Wacs.Core.Attributes;

namespace Wacs.Core.OpCodes
{
    public enum ExtCode : byte
    {
        [OpCode("i32.trunc_sat_f32_s")] I32TruncSatF32S = 0x00,
        [OpCode("i32.trunc_sat_f32_u")] I32TruncSatF32U = 0x01,
        [OpCode("i32.trunc_sat_f64_s")] I32TruncSatF64S = 0x02,
        [OpCode("i32.trunc_sat_f64_u")] I32TruncSatF64U = 0x03,
        [OpCode("i64.trunc_sat_f32_s")] I64TruncSatF32S = 0x04,
        [OpCode("i64.trunc_sat_f32_u")] I64TruncSatF32U = 0x05,
        [OpCode("i64.trunc_sat_f64_s")] I64TruncSatF64S = 0x06,
        [OpCode("i64.trunc_sat_f64_u")] I64TruncSatF64U = 0x07,
    
        [OpCode("memory.init")]         MemoryInit = 0x08,
        [OpCode("data.drop")]           DataDrop = 0x09,
        [OpCode("memory.copy")]         MemoryCopy = 0x0A,
        [OpCode("memory.fill")]         MemoryFill = 0x0B,
   
        [OpCode("table.init")]          TableInit = 0x0C,
        [OpCode("elem.drop")]           ElemDrop = 0x0D,
        [OpCode("table.copy")]          TableCopy = 0x0E,
        [OpCode("table.grow")]          TableGrow = 0x0F,
        [OpCode("table.size")]          TableSize = 0x10,
        [OpCode("table.fill")]          TableFill = 0x11
    }

}