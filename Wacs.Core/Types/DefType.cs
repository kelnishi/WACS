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
using System.Linq;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Types
{
    public class DefType
    {
        public readonly RecursiveType RecType;
        public TypeIdx DefIndex = TypeIdx.Default;
        private int Projection;

        public CompositeType Expansion;

        public DefType(RecursiveType recType, int proj)
        {
            RecType = recType;
            Projection = proj;
            
            Expansion = RecType.SubTypes[Projection].Body;
        }

        public SubType Unroll => RecType.SubTypes[Projection];

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#defined-types⑤
        /// </summary>
        /// <param name="other"></param>
        /// <param name="types"></param>
        /// <returns></returns>
        public bool Matches(DefType other, TypesSpace? types)
        {
            if (DefIndex == TypeIdx.Default)
                throw new InvalidDataException("DefType was not finalized before validation");
            
            return DefIndex == other.DefIndex || Unroll.SuperTypes.Any(super => DefIndex.Matches(super, types));
        }
        
    }
}