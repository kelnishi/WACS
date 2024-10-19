using System.Numerics;

namespace Wacs.Core.Utilities
{
    public static class TypeUtilities
    {
        public static (ulong, ulong) ToV128(this BigInteger bigint)
        {
            ulong low = (ulong)(bigint & ulong.MaxValue);
            ulong high = (ulong)(bigint >> 64);
            return (low, high);
        }
    }
}