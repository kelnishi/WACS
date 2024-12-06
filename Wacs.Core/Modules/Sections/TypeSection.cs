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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.2. Types
        /// </summary>
        public List<RecursiveType> Types { get; internal set; } = new();

        public List<DefType> UnrollTypes()
        {
            var defs = new List<DefType>();
            for (int r = 0, t = Types.Count; r < t; ++r)
            {
                var recType = Types[r];
                for (int s = 0, l = recType.SubTypes.Length; s < l; ++s)
                {
                    defs.Add(new DefType(recType, s));
                }
            }
            return defs;
        }
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.4 Type Section
        /// </summary>
        private static List<RecursiveType> ParseTypeSection(BinaryReader reader) => 
            reader.ParseList(RecursiveType.Parse);
    }
}