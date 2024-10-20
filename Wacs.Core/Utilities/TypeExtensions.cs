using System.Linq;
using System.Numerics;

namespace Wacs.Core.Utilities
{
    public static class TypeUtilities
    {
        public static (
            byte, byte, byte, byte,
            byte, byte, byte, byte,
            byte, byte, byte, byte,
            byte, byte, byte, byte
            ) ToV128(this BigInteger bigint)
        {
            byte[] bytes = bigint.ToByteArray();
            return (
                bytes.ElementAtOrDefault(0x0),
                bytes.ElementAtOrDefault(0x1),
                bytes.ElementAtOrDefault(0x2),
                bytes.ElementAtOrDefault(0x3),
                bytes.ElementAtOrDefault(0x4),
                bytes.ElementAtOrDefault(0x5),
                bytes.ElementAtOrDefault(0x6),
                bytes.ElementAtOrDefault(0x7),
                bytes.ElementAtOrDefault(0x8),
                bytes.ElementAtOrDefault(0x9),
                bytes.ElementAtOrDefault(0xA),
                bytes.ElementAtOrDefault(0xB),
                bytes.ElementAtOrDefault(0xC),
                bytes.ElementAtOrDefault(0xD),
                bytes.ElementAtOrDefault(0xE),
                bytes.ElementAtOrDefault(0xF)
            );
        }
    }
}