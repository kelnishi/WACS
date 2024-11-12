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

using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    [WasmType(nameof(ValType.I32))]
    public enum Whence : byte
    {
        /// <summary>
        /// Seek relative to start-of-file
        /// </summary>
        Set = 0,
        
        /// <summary>
        /// Seek relative to current position.
        /// </summary>
        Cur = 1,
        
        /// <summary>
        /// Seek relative to end-of-file.
        /// </summary>
        End = 2,
    }
}