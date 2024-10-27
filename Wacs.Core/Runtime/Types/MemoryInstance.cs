using System;
using System.Runtime.InteropServices;
using System.Text;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime.Types
{
    public class MemoryInstance
    {
        private byte[] _data;

        public MemoryInstance(MemoryType type)
        {
            Type = type;
            uint initialSize = type.Limits.Minimum * Constants.PageSize;
            _data = new byte[initialSize];
        }

        public MemoryType Type { get; }
        public byte[] Data => _data;

        public long Size => _data.Length / Constants.PageSize;

        public Span<byte> this[Range range]
        {
            get
            {
                var (start, length) = range.GetOffsetAndLength(_data.Length);
                Span<byte> span = _data[start..(start+length)];
                return span;
            }
        }

        /// <summary>
        /// @Spec 4.5.3.9. Growing memories
        /// </summary>
        public bool Grow(uint numPages)
        {
            uint oldSize = (uint)(Data.Length / Constants.PageSize);
            uint len = oldSize + numPages;

            if (len > Type.Limits.Maximum)
            {
                return false; // Cannot grow beyond maximum limit
            }

            var newLimits = new Limits(Type.Limits)
            {
                Minimum = len
            };
            var validator = TableType.Validator.Limits;
            try
            {
                validator.ValidateAndThrow(newLimits);
            }
            catch (ValidationException exc)
            {
                _ = exc;
                return false;
            }

            Array.Resize(ref _data, (int)(len * Constants.PageSize));

            return true;
        }

        public bool Contains(int offset, int width) => 
            offset >= 0 && (offset + width) <= _data.Length;

        public string ReadString(uint ptr, uint len)
        {
            var bytes = this[(int)ptr..(int)(ptr + len)];
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public int WriteString(int ptr, string str) => WriteString((uint)ptr, str);

        public int WriteString(uint ptr, string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            var bytes = this[(int)ptr..(int)(ptr + data.Length + 1)];
            data.CopyTo(bytes);
            //null terminate strings
            bytes[^1] = 0;
            return bytes.Length;
        }

        public int WriteStruct<T>(int ptr, ref T str) where T : struct => WriteStruct((uint)ptr, ref str);

        public int WriteStruct<T>(uint ptr, ref T str)
            where T: struct
        {
            int size = Marshal.SizeOf<T>();
            var buf = this[(int)ptr..(int)(ptr + size)];
            MemoryMarshal.Write(buf, ref str);
            return size;
        }

        public void WriteInt32(int ptr, int x) => WriteInt32((uint)ptr, (uint)x);
        public void WriteInt32(int ptr, uint x) => WriteInt32((uint)ptr, x);
        public void WriteInt32(uint ptr, int x) => WriteInt32(ptr, (uint)x);

        public void WriteInt32(uint ptr, uint x)
        {
            if (!Contains((int)ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var buf = this[(int)ptr..(int)(ptr + sizeof(uint))];
            buf[0] = (byte) (x & 0xFF);
            buf[1] = (byte)((x >> 8) & 0xFF);
            buf[2] = (byte)((x >> 16) & 0xFF);
            buf[3] = (byte)((x >> 24) & 0xFF);
        }

        public void WriteInt64(int ptr, long x) => WriteInt64((uint)ptr, (ulong)x);
        public void WriteInt64(int ptr, ulong x) => WriteInt64((uint)ptr, x);
        public void WriteInt64(uint ptr, long x) => WriteInt64(ptr, (ulong)x);

        public void WriteInt64(uint ptr, ulong x)
        {
            if (!Contains((int)ptr, sizeof(ulong)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var buf = this[(int)ptr..(int)(ptr + sizeof(ulong))];
            buf[0] = (byte) (x & 0xFF);
            buf[1] = (byte)((x >> 8) & 0xFF);
            buf[2] = (byte)((x >> 16) & 0xFF);
            buf[3] = (byte)((x >> 24) & 0xFF);
            buf[4] = (byte)((x >> 32) & 0xFF);
            buf[5] = (byte)((x >> 40) & 0xFF);
            buf[6] = (byte)((x >> 48) & 0xFF);
            buf[7] = (byte)((x >> 56) & 0xFF);
        }
    }
}