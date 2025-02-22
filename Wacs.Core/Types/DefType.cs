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
using System.Linq;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    public class DefType
    {
        public readonly TypeIdx DefIndex;
        private readonly int Projection;
        public readonly RecursiveType RecType;

        private int _computedHash;

        public CompositeType Expansion;

        public List<DefType> SuperTypes;

        public DefType(RecursiveType recType, int proj, TypeIdx def)
        {
            RecType = recType;
            Projection = proj;
            DefIndex = def;
            Expansion = RecType.SubTypes[Projection].Body;

            var hash = new StableHash();
            hash.Add(nameof(DefType));
            _computedHash = hash.ToHashCode();
        }

        public SubType Unroll => RecType.SubTypes[Projection];

        public void ComputeHash()
        {
            var hash = new StableHash();
            hash.Add(nameof(DefType));
            hash.Add(RecType.GetHashCode());
            hash.Add(Projection);
            _computedHash = hash.ToHashCode();
        }

        public override int GetHashCode() => _computedHash;

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#defined-types⑤
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#rolling-and-unrolling①
        /// </summary>
        /// <param name="other"></param>
        /// <param name="types"></param>
        /// <returns></returns>
        public bool Matches(DefType other, TypesSpace? types)
        {
            if (GetHashCode() == other.GetHashCode())
                return true;
            if (SuperTypes.Any(super => super.Matches(other, types))) 
                return true;
            return false;
        }
    }
}