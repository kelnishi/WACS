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
    public enum ClockId : int
    {
        /// <summary>
        /// The clock measuring real time. Time value zero corresponds with 1970-01-01T00:00:00Z.
        /// </summary>
        Realtime = 0,
        
        /// <summary>
        /// The store-wide monotonic clock, which is defined as a clock measuring real time, whose value cannot be
        /// adjusted and which cannot have negative clock jumps. The epoch of this clock is undefined. The absolute
        /// time value of this clock therefore has no meaning.
        /// </summary>
        Monotonic = 1,
        
        /// <summary>
        /// The CPU-time clock associated with the current process.
        /// </summary>
        ProcessCputimeId = 2,
        
        /// <summary>
        /// The CPU-time clock associated with the current thread.
        /// </summary>
        ThreadCputimeId = 3,
    }
}