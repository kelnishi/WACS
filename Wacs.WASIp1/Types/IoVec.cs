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
            this = MemoryMarshal.Cast<ulong, IoVec>(MemoryMarshal.CreateSpan(ref bits, 1))[0];
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