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
using System.IO;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    public class SubType
    {
        public readonly bool Final;
        public readonly TypeIdx[] TypeIndexes;
        public readonly CompositeType CmpType;

        public SubType(TypeIdx[] idxs, CompositeType cmpType, bool final)
        {
            TypeIndexes = idxs;
            CmpType = cmpType;
            Final = final;
        }
        
        public SubType(CompositeType cmpType, bool final)
        {
            TypeIndexes = Array.Empty<TypeIdx>();
            CmpType = cmpType;
            Final = final;
        }
        
        public static TypeIdx ParseTypeIndexes(BinaryReader reader) => 
            (TypeIdx)reader.ReadLeb128_u32();
        
        public static SubType Parse(BinaryReader reader)
        {
            return reader.ReadByte() switch
            {
                (byte)RecType.SubXCt => new SubType(
                    reader.ParseVector(ParseTypeIndexes),
                    CompositeType.ParseTagged(reader),
                    false
                ),
                (byte)RecType.SubFinalXCt => new SubType(
                    reader.ParseVector(ParseTypeIndexes),
                    CompositeType.ParseTagged(reader),
                    true
                ),
                (byte)CompType.ArrayAt => new SubType(ArrayType.Parse(reader), true),
                (byte)CompType.StructSt => new SubType(StructType.Parse(reader), true),
                (byte)CompType.FuncFt => new SubType(FunctionType.Parse(reader), true),
                
                var form => throw new FormatException(
                    $"Invalid type format {form} at offset {reader.BaseStream.Position-1}.")
            };
        }

        public bool Matches(CompositeType type)
        {
            //TODO: Compute type heirarchy
            return true;
        }
        
        
        public ValType Ref =>
            CmpType switch
            {
                FunctionType ft => ValType.Func,
                ArrayType at => ValType.Array,
                StructType st => ValType.Struct,
                _ => ValType.None,
            };
    }
}