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
using Wacs.Core.Types.Defs;

namespace Wacs.WASIp1.Types
{
    
    [WasmType(nameof(ValType.I32))]
    public enum Advice : byte
    {
        /// <summary>
        /// The application has no advice to give on its behavior with respect to the specified data.
        /// </summary>
        Normal     = 0, 

        /// <summary>
        /// The application expects to access the specified data sequentially from lower offsets to higher offsets.
        /// </summary>
        Sequential = 1, 

        /// <summary>
        /// The application expects to access the specified data in a random order.
        /// </summary>
        Random     = 2, 

        /// <summary>
        /// The application expects to access the specified data in the near future.
        /// </summary>
        WillNeed   = 3, 

        /// <summary>
        /// The application expects that it will not access the specified data in the near future.
        /// </summary>
        DontNeed   = 4, 

        /// <summary>
        /// The application expects to access the specified data once and then not reuse it thereafter.
        /// </summary>
        NoReuse    = 5  
    }
}