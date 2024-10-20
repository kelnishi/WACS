using System.IO;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    public partial class ReferenceFactory
    {
        public static IInstruction? CreateInstruction(SimdCode opcode) => opcode switch
        {
            _ => throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFD{(byte)opcode:X2}")
        };
    }
}