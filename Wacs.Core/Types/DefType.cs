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

namespace Wacs.Core.Types
{
    public class DefType
    {
        private RecursiveType RecType;
        private int Projection;

        public CompositeType Expansion;

        public DefType(RecursiveType recType, int proj)
        {
            RecType = recType;
            Projection = proj;
            
            Expansion = RecType.SubTypes[Projection].Body;
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#defined-types⑤
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Matches(DefType other)
        {
            return false;
        }
        
    }
}