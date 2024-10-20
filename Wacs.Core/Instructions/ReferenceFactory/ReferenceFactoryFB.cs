using System.IO;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    public partial class ReferenceFactory
    {
        public static IInstruction? CreateInstruction(GcCode opcode) => opcode switch
        {
            _ => throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFB{(byte)opcode:X2}")
        };
    }
}