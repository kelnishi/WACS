using Wacs.Core.Attributes;

namespace Wacs.Core.OpCodes
{
    /// <summary>
    /// Represents all WebAssembly opcodes for the FB prefix
    /// Theoretically, these could be u32, but I'll keep them as bytes so long as they fit.
    /// </summary>
    public enum GcCode : byte
    {
        [OpCode("struct.new")]          StructNew           = 0x00,
        [OpCode("struct.new_default")]  StructNewDefault    = 0x01,
        [OpCode("struct.get")]          StructGet           = 0x02,
        [OpCode("struct.get_s")]        StructGetS          = 0x03,
        [OpCode("struct.get_u")]        StructGetU          = 0x04,
        [OpCode("struct.set")]          StructSet           = 0x05,
        [OpCode("array.new")]           ArrayNew            = 0x06,
        [OpCode("array.new_default")]   ArrayNewDefault     = 0x07,
        [OpCode("array.new_fixed")]     ArrayNewFixed       = 0x08,
        [OpCode("array.new_data")]      ArrayNewData        = 0x09,
        [OpCode("array.new_elem")]      ArrayNewElem        = 0x0A,
        [OpCode("array.get")]           ArrayGet            = 0x0B,
        [OpCode("array.get_s")]         ArrayGetS           = 0x0C,
        [OpCode("array.get_u")]         ArrayGetU           = 0x0D,
        [OpCode("array.set")]           ArraySet            = 0x0E,
        [OpCode("array.len")]           ArrayLen            = 0x0F,
        [OpCode("array.fill")]          ArrayFill           = 0x10,
        [OpCode("array.copy")]          ArrayCopy           = 0x11,
        [OpCode("array.init_data")]     ArrayInitData       = 0x12,
        [OpCode("array.init_elem")]     ArrayInitElem       = 0x13,
        [OpCode("ref.test")]            RefTest             = 0x14,
        [OpCode("ref.test (ref null)")] RefTestNull         = 0x15,
        [OpCode("ref.cast")]            RefCast             = 0x16,
        [OpCode("ref.cast (ref null)")] RefCastNull         = 0x17,
        [OpCode("br_on_cast")]          BrOnCast            = 0x18,
        [OpCode("br_on_cast_fail")]     BrOnCastFail        = 0x19,
        [OpCode("any.convert_extern")]  AnyConvertExtern    = 0x1A,
        [OpCode("extern.convert_any")]  ExternConvertAny    = 0x1B,
        [OpCode("ref.i31")]             RefI31              = 0x1C,
        [OpCode("i31.get_s")]           I31GetS             = 0x1D,
        [OpCode("i31.get_u")]           I31GetU             = 0x1E,
    }

}