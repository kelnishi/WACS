using System;
using System.Runtime.InteropServices;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    /// <summary>
    /// A region of memory for scatter/gather reads.
    /// </summary>
    [WasmType(nameof(ValType.I64))]
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct IoVec : ITypeConvertable
    {
        [FieldOffset(0)]
        public uint bufPtr;
        
        [FieldOffset(4)]
        public uint bufLen;

        public void FromWasmValue(object value)
        {
            ulong bits = (ulong)value;
            byte[] bytes = BitConverter.GetBytes(bits);
            this = MemoryMarshal.Cast<byte, IoVec>(bytes.AsSpan())[0];
        }

        public Value ToWasmType()
        {
            byte[] bytes = new byte[8];
            MemoryMarshal.Write(bytes, ref this);
            return MemoryMarshal.Read<ulong>(bytes);
        }
    }
    
    /// <summary>
    /// A region of memory for scatter/gather writes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct CIoVec
    {
        [FieldOffset(0)]
        public uint bufPtr;
        
        [FieldOffset(4)]
        public uint bufLen;
    }
}