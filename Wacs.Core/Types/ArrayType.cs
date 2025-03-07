// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using System.IO;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    public class ArrayType : CompositeType
    {
        public readonly FieldType ElementType;

        public ArrayType(FieldType ft)
        {
            ElementType = ft;
        }

        public static ArrayType Parse(BinaryReader reader) => 
            new(FieldType.Parse(reader));

        public bool Matches(ArrayType other, TypesSpace? types)
        {
            return ElementType.Matches(other.ElementType, types);
        }

        public override int ComputeHash(int defIndexValue, List<DefType> defs)
        {
            var hash = new StableHash();
            hash.Add(nameof(ArrayType));
            hash.Add(ElementType.ComputeHash(defIndexValue,defs));
            return hash.ToHashCode();
        }
    }
}