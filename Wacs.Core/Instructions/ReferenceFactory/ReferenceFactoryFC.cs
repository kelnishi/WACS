using System.IO;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    public partial class ReferenceFactory
    {
        public static IInstruction? CreateInstruction(ExtCode opcode) => opcode switch
        {
            //Non-Trapping Saturating Float to Int Conversion 
            ExtCode.I32TruncSatF32S   => NumericInst.I32TruncSatF32S,
            ExtCode.I32TruncSatF32U   => NumericInst.I32TruncSatF32U,
            ExtCode.I32TruncSatF64S   => NumericInst.I32TruncSatF64S,
            ExtCode.I32TruncSatF64U   => NumericInst.I32TruncSatF64U,
            ExtCode.I64TruncSatF32S   => NumericInst.I64TruncSatF32S,
            ExtCode.I64TruncSatF32U   => NumericInst.I64TruncSatF32U,
            ExtCode.I64TruncSatF64S   => NumericInst.I64TruncSatF64S,
            ExtCode.I64TruncSatF64U   => NumericInst.I64TruncSatF64U,
            
            //Memory Instructions
            ExtCode.MemoryInit        => new InstMemoryInit(),
            ExtCode.DataDrop          => new InstDataDrop(),
            ExtCode.MemoryCopy        => new InstMemoryCopy(),
            ExtCode.MemoryFill        => new InstMemoryFill(),
            
            //Table Instructions
            ExtCode.TableInit         => new InstTableInit(),
            ExtCode.ElemDrop          => new InstElemDrop(),
            ExtCode.TableCopy         => new InstTableCopy(),
            ExtCode.TableGrow         => new InstTableGrow(),
            ExtCode.TableSize         => new InstTableSize(),
            ExtCode.TableFill         => new InstTableFill(),
            _ => 
                throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFC{(byte)opcode:X2}")
        };
    }
}