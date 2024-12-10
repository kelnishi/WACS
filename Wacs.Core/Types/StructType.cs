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
using System.Linq;
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    public class StructType : CompositeType
    {
        public readonly FieldType[] FieldTypes;
        public int Arity => FieldTypes.Length;

        public StructType(FieldType[] types)
        {
            FieldTypes = types;
        }

        public static StructType Parse(BinaryReader reader) => 
            new(reader.ParseVector(FieldType.Parse));


        public bool Matches(StructType other, TypesSpace? types)
        {
            if (Arity < other.Arity)
                return false;
            foreach (var (ft, index) in other.FieldTypes.Select((t, i) => (t, i)))
            {
                if (!ft.Matches(FieldTypes[index], types))
                    return false;
            }
            return true;
        }
    }
}