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

using System.IO;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    public struct MemArg
    {
        public Alignment Align;
        public MemIdx M;
        public long Offset;
        
        public MemArg(Alignment align, long offset, MemIdx idx)
        {
            Align = align;
            M = idx;
            Offset = offset;
        }

        public static MemArg Parse(BinaryReader reader)
        {
            uint bits = reader.ReadLeb128_u32();
            if ((bits & (uint)Alignment.LogBits) > 16)
                throw new InvalidDataException($"Invalid memory alignment");
            
            Alignment align = (Alignment)bits;
            if (align.ExpSize() > Constants.PageSize)
                throw new InvalidDataException($"Memory alignment exceeds page size");

            MemIdx idx = default;

            if (align.HasFlag(Alignment.MemIdxSet))
            {
                idx = (MemIdx)reader.ReadLeb128_s32();
            }

            long offset = (long)reader.ReadLeb128_u64();
            return new MemArg(align, offset, idx);
        }

        public string ToWat(BitWidth naturalAlign)
        {
            var offset = Offset != 0 ? $" offset={Offset}" : "";
            var align = Align.LinearSize() != naturalAlign.ByteSize() ? $" align={Align}" : "";
            return $"{offset}{align}";
        }
    }
}