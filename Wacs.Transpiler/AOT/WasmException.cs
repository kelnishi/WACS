// Copyright 2025 Kelvin Nishikawa
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

using System;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// CLR exception type for WASM exceptions thrown by transpiled code.
    ///
    /// WASM exceptions carry a tag address (identifying the exception type)
    /// and field values (the exception payload). This type bridges between
    /// WASM's structured exception model and CIL's throw/catch mechanism.
    ///
    /// Spec: An exception instance is {tag: tagaddr, fields: val*}.
    /// throw creates the instance and initiates unwinding.
    /// try_table catch clauses match on tag and extract fields.
    /// </summary>
    public class WasmException : Exception
    {
        /// <summary>Tag address identifying the exception type.</summary>
        public TagAddr Tag { get; }

        /// <summary>Exception field values (the payload).</summary>
        public Value[] Fields { get; }

        /// <summary>The exnref Value for catch_ref/catch_all_ref clauses.</summary>
        public Value ExnRef { get; }

        public WasmException(TagAddr tag, Value[] fields, Value exnRef)
            : base($"wasm exception tag={tag.Value}")
        {
            Tag = tag;
            Fields = fields;
            ExnRef = exnRef;
        }
    }
}
