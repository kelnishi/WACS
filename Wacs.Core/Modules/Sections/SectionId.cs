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

namespace Wacs.Core
{
    /// <summary>
    /// @Spec 5.5.2 Sections
    /// </summary>
    public enum SectionId : byte
    {
        Type = 1,            // Type section    
        Import = 2,          // Import section
        Function = 3,        // Function section
        Table = 4,           // Table section
        Memory = 5,          // Memory section
        Tag = 13,            // Tag section
        Global = 6,          // Global section
        Export = 7,          // Export section
        Start = 8,           // Start section
        Element = 9,         // Element section
        DataCount = 12,      // Data count section (if applicable)
        Code = 10,           // Code section
        Data = 11,           // Data section
        Custom = 0,          // Custom sections
    }
}