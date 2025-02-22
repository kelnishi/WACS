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

using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    /// <summary>
    /// @Spec 2.5.9. Start Function
    /// </summary>
    public partial class Module
    {
        public FuncIdx StartIndex { get; internal set; } = FuncIdx.Default;
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.11 Start Section
        /// </summary>
        private static FuncIdx ParseStartSection(BinaryReader reader) =>
            (FuncIdx)reader.ReadLeb128_u32();
    }
}