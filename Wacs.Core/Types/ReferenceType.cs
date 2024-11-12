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
using Wacs.Core.Attributes;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.3 Reference Types
    /// Represents the reference types used in table elements.
    /// </summary>
    public enum ReferenceType : byte
    {
        /// <summary>
        /// Function reference.
        /// </summary>
        [WatToken("funcref")] Funcref = 0x70,

        /// <summary>
        /// External reference.
        /// </summary>
        [WatToken("externref")] Externref = 0x6F,

        // Additional reference types from future proposals can be added here.
    }

    public enum HeapType : byte
    {
        [WatToken("func")] Funcref = ReferenceType.Funcref,
        
        [WatToken("extern")] Externref = ReferenceType.Externref,
    }
    
    public static class ReferenceTypeParser
    {
        public static ReferenceType Parse(BinaryReader reader) =>
            (ReferenceType)reader.ReadByte() switch
            {
                ReferenceType.Funcref => ReferenceType.Funcref,
                ReferenceType.Externref => ReferenceType.Externref,
                _ => throw new FormatException($"Invalid reference type: {reader.ReadByte():x8}"),
            };
    }

    public static class ReferenceTypeExtensions
    {
        public static ValType StackType(this ReferenceType reftype)=> reftype switch
            {
                ReferenceType.Funcref => ValType.Funcref,
                ReferenceType.Externref => ValType.Externref,
                _ => throw new InvalidDataException($"ReferenceType {reftype} is invalid.")
            };
    }
}