using System;

namespace Wacs.Core.Utilities
{
    public static class MemoryExtensions
    {
        public static void WriteInt32(this Span<byte> span, int value, int startIndex = 0) =>
            WriteInt32(span, (uint)value, startIndex);

        public static void WriteInt32(this Span<byte> span, uint value, int startIndex = 0)
        {
            if (span.Length < startIndex + sizeof(int))
            {
                throw new ArgumentException("Span is too small for the operation.");
            }
        
            span[startIndex] = (byte)(value & 0xFF);
            span[startIndex + 1] = (byte)((value >> 8) & 0xFF);
            span[startIndex + 2] = (byte)((value >> 16) & 0xFF);
            span[startIndex + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static void WriteInt64(this Span<byte> span, long value, int startIndex = 0) =>
            WriteInt64(span, (ulong)value, startIndex);

        public static void WriteInt64(this Span<byte> span, ulong value, int startIndex = 0)
        {
            if (span.Length < startIndex + sizeof(long))
            {
                throw new ArgumentException("Span is too small for the operation.");
            }
        
            span[startIndex] = (byte)(value & 0xFF);
            span[startIndex + 1] = (byte)((value >> 8) & 0xFF);
            span[startIndex + 2] = (byte)((value >> 16) & 0xFF);
            span[startIndex + 3] = (byte)((value >> 24) & 0xFF);
            span[startIndex + 4] = (byte)((value >> 32) & 0xFF);
            span[startIndex + 5] = (byte)((value >> 40) & 0xFF);
            span[startIndex + 6] = (byte)((value >> 48) & 0xFF);
            span[startIndex + 7] = (byte)((value >> 56) & 0xFF);
        }
    }
}