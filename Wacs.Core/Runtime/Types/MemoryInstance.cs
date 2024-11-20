// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using FluentValidation;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime.Types
{
    public class MemoryInstance
    {
        public byte[] Data;

        [SuppressMessage("ReSharper.DPA", "DPA0003: Excessive memory allocations in LOH", MessageId = "type: System.Byte[]; size: 134MB")]
        public MemoryInstance(MemoryType type)
        {
            Type = type;

            if (type.Limits.Minimum > Constants.HostMaxPages)
                throw new InstantiationException($"Cannot allocate memory of size {type.Limits.Minimum}");
            
            uint initialSize = (type.Limits.Minimum)* Constants.PageSize;
            Data = new byte[initialSize];
        }

        public MemoryType Type { get; private set; }

        public long Size => Data.Length / Constants.PageSize;

        //TODO bounds checking?
        public Span<byte> this[Range range] => Data.AsSpan(range);

        /// <summary>
        /// @Spec 4.5.3.9. Growing memories
        /// </summary>
        public bool Grow(uint numPages)
        {
            uint oldNumPages = (uint)(Data.Length / Constants.PageSize);
            uint newNumPages = oldNumPages + numPages;

            if (newNumPages > Constants.HostMaxPages)
                return false;
            
            if (newNumPages > Type.Limits.Maximum)
                return false;
            
            var newLimits = new Limits(Type.Limits)
            {
                Minimum = newNumPages
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

            int len = (int)(newNumPages * Constants.PageSize);

            Array.Resize(ref Data, len);

            Type = new MemoryType(newLimits);

            return true;
        }

        public bool Contains(int offset, int width) => 
            offset >= 0 && (offset + width) <= Data.Length;

        public string ReadString(uint ptr, uint len)
        {
            var bytes = this[(int)ptr..(int)(ptr + len)];
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public int WriteUtf8String(uint ptr, string str, bool nullTerminate = false)
        {
            var data = Encoding.UTF8.GetBytes(str);
            Buffer.BlockCopy(data, 0, Data, (int)ptr, data.Length);
            if (nullTerminate)
                Data[ptr + data.Length] = 0;
            return data.Length + (nullTerminate?1:0);
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

        public void WriteInt32(int ptr, int x)
        {
            if (!Contains(ptr, sizeof(int)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(int));
        }

        public void WriteInt32(uint ptr, int x)
        {
            if (!Contains((int)ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(int));
        }

        public void WriteInt32(int ptr, uint x)
        {
            if (!Contains(ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(uint));
        }

        public void WriteInt32(uint ptr, uint x)
        {
            if (!Contains((int)ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(uint));
        }

        public void WriteInt64(int ptr, long x)
        {
            if (!Contains(ptr, sizeof(long)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(long));
        }

        public void WriteInt64(uint ptr, long x)
        {
            if (!Contains((int)ptr, sizeof(long)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(long));
        }

        public void WriteInt64(int ptr, ulong x)
        {
            if (!Contains(ptr, sizeof(ulong)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(ulong));
        }

        public void WriteInt64(uint ptr, ulong x)
        {
            if (!Contains((int)ptr, sizeof(ulong)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(ulong));
        }

        public T[] ReadStructs<T>(uint iovsPtr, uint iovsLen)
            where T : struct
        {
            int size = Marshal.SizeOf<T>();
    
            long span = size * iovsLen;
            if (span > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(iovsLen), "Resulting array size exceeds maximum allowed size.");
            }

            int byteCount = (int)span;

            // Ensure access is within bounds here (for example, check if (iovsPtr + byteCount) is within allowed range)
            var bytes = this[(int)iovsPtr..(int)(iovsPtr + byteCount)];
    
            // Use MemoryMarshal to cast the byte array to an array of T
            var array = MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    
            return array;
        }
    }
}