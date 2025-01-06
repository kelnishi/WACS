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

namespace Wacs.Core.Types.Defs
{
    /// <summary>
    /// @Spec 5.5.5 Import Section
    /// Represents the kinds of external values that can be exported or imported.
    /// </summary>
    public enum ExternalKind : byte
    {
        /// <summary>
        /// A function external kind.
        /// </summary>
        Function = 0x00,

        /// <summary>
        /// A table external kind.
        /// </summary>
        Table = 0x01,

        /// <summary>
        /// A memory external kind.
        /// </summary>
        Memory = 0x02,

        /// <summary>
        /// A global external kind.
        /// </summary>
        Global = 0x03,
        
        /// <summary>
        /// https://github.com/WebAssembly/exception-handling/blob/main/proposals/exception-handling/Exceptions.md#external_kind
        /// </summary>
        Tag = 0x04
    }

    public static class ExternalKindParser
    {
        /// <summary>
        /// @Spec 5.5.5 Import Section
        /// </summary>
        public static ExternalKind Parse(BinaryReader reader) =>
            (ExternalKind)reader.ReadByte() switch
            {
                ExternalKind.Function => ExternalKind.Function,
                ExternalKind.Table => ExternalKind.Table,
                ExternalKind.Memory => ExternalKind.Memory,
                ExternalKind.Global => ExternalKind.Global,
                ExternalKind.Tag => ExternalKind.Tag,
                _ => throw new FormatException($"Invalid Import kind type at offset {reader.BaseStream.Position}.")
            };
    }
}