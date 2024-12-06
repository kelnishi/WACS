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

namespace Wacs.Core.Types
{
    public abstract class CompositeType
    {
        public static CompositeType ParseTagged(BinaryReader reader) =>
            reader.ReadByte() switch
            {
                (byte)CompType.ArrayAt => ArrayType.Parse(reader),
                (byte)CompType.StructSt => StructType.Parse(reader),
                (byte)CompType.FuncFt => FunctionType.Parse(reader),
                var form => throw new FormatException(
                    $"Invalid comptype format {form} at offset {reader.BaseStream.Position - 1}.")
            };
    }
}