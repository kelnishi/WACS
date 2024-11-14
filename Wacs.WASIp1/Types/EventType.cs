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

namespace Wacs.WASIp1.Types
{
    public enum EventType : byte
    {
        /// <summary>
        /// The time value of the clock subscription has reached the specified timeout timestamp.
        /// </summary>
        Clock = 0,

        /// <summary>
        /// Indicates that the file descriptor has data available for reading.
        /// This event always triggers for regular files.
        /// </summary>
        FdRead = 1,

        /// <summary>
        /// Indicates that the file descriptor has capacity available for writing.
        /// This event always triggers for regular files.
        /// </summary>
        FdWrite = 2
    }
}