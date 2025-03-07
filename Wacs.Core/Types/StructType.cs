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
using System.Linq;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    public class StructType : CompositeType
    {
        public readonly FieldType[] FieldTypes;

        public StructType(FieldType[] types)
        {
            FieldTypes = types;
        }

        public int Arity => FieldTypes.Length;

        public FieldType this[FieldIdx y] => FieldTypes[y.Value];

        public static StructType Parse(BinaryReader reader) => 
            new(reader.ParseVector(FieldType.Parse));

        public bool Matches(StructType other, TypesSpace? types)
        {
            if (Arity < other.Arity)
                return false;
            foreach (var (ft, index) in other.FieldTypes.Select((t, i) => (t, i)))
            {
                if (!FieldTypes[index].Matches(ft, types))
                    return false;
            }
            return true;
        }

        public override int ComputeHash(int defIndexValue, List<DefType> defs)
        {
            var hash = new StableHash();
            hash.Add(nameof(StructType));
            foreach (var (ft, index) in FieldTypes.Select((t, i) => (t, i)))
            {
                hash.Add(index);
                hash.Add(ft.ComputeHash(defIndexValue, defs));
            }
            return hash.ToHashCode();
        }
    }
}