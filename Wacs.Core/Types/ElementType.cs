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
    public enum ElementType: uint
    {
        /// <summary>
        /// **Flag Value 0 (0x00)**:
        /// - **Active Segment**
        /// - **Table Index**: Implicitly 0 (table index is not present)
        /// - **Offset Expression**: Present
        /// - **Element Kind**: Present (byte `0x00` indicating `func`)
        /// - **Element Type**: Not present
        /// - **Elements**: Vector of function indices (`vec(funcidx)`)
        /// 
        /// This is the simplest form of an active element segment targeting the default table.
        /// </summary>
        ActiveNoIndexWithElemKind = 0,

        /// <summary>
        /// **Flag Value 1 (0x01)**:
        /// - **Passive Segment**
        /// - **Table Index**: Not present
        /// - **Offset Expression**: Not present
        /// - **Element Kind**: Present (`0x00`)
        /// - **Element Type**: Not present
        /// - **Elements**: Vector of function indices (`vec(funcidx)`)
        /// 
        /// The segment is passive and can be applied to tables via instructions like `table.init`.
        /// </summary>
        PassiveWithElemKind = 1,

        /// <summary>
        /// **Flag Value 2 (0x02)**:
        /// - **Active Segment**
        /// - **Table Index**: Present (`tableidx`)
        /// - **Offset Expression**: Present
        /// - **Element Kind**: Present (`0x00`)
        /// - **Element Type**: Not present
        /// - **Elements**: Vector of function indices (`vec(funcidx)`)
        /// 
        /// Active segment targeting a specified table.
        /// </summary>
        ActiveWithIndexAndElemKind = 2,

        /// <summary>
        /// **Flag Value 3 (0x03)**:
        /// - **Declarative Segment**
        /// - **Table Index**: Not present
        /// - **Offset Expression**: Not present
        /// - **Element Kind**: Present (`0x00`)
        /// - **Element Type**: Not present
        /// - **Elements**: Vector of function indices (`vec(funcidx)`)
        /// 
        /// The segment is declarative and is not used to initialize any table.
        /// </summary>
        DeclarativeWithElemKind = 3,

        /// <summary>
        /// **Flag Value 4 (0x04)**:
        /// - **Active Segment**
        /// - **Table Index**: Implicitly 0
        /// - **Offset Expression**: Present
        /// - **Element Kind**: Not present
        /// - **Element Type**: Present (`reftype`)
        /// - **Elements**: Vector of expressions (`vec(expr)`)
        /// 
        /// Active segment with elements of a reference type, using expressions.
        /// </summary>
        ActiveNoIndexWithElemType = 4,

        /// <summary>
        /// **Flag Value 5 (0x05)**:
        /// - **Passive Segment**
        /// - **Table Index**: Not present
        /// - **Offset Expression**: Not present
        /// - **Element Kind**: Not present
        /// - **Element Type**: Present (`reftype`)
        /// - **Elements**: Vector of expressions (`vec(expr)`)
        /// 
        /// Passive segment with elements of a reference type.
        /// </summary>
        PassiveWithElemType = 5,

        /// <summary>
        /// **Flag Value 6 (0x06)**:
        /// - **Active Segment**
        /// - **Table Index**: Present (`tableidx`)
        /// - **Offset Expression**: Present
        /// - **Element Kind**: Not present
        /// - **Element Type**: Present (`reftype`)
        /// - **Elements**: Vector of expressions (`vec(expr)`)
        /// 
        /// Active segment targeting a specified table with reference type elements.
        /// </summary>
        ActiveWithIndexAndElemType = 6,

        /// <summary>
        /// **Flag Value 7 (0x07)**:
        /// - **Declarative Segment**
        /// - **Table Index**: Not present
        /// - **Offset Expression**: Not present
        /// - **Element Kind**: Not present
        /// - **Element Type**: Present (`reftype`)
        /// - **Elements**: Vector of expressions (`vec(expr)`)
        /// 
        /// Declarative segment with reference type elements, not used to initialize tables.
        /// </summary>
        DeclarativeWithElemType = 7
    }

}