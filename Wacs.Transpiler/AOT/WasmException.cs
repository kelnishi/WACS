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
    /// WASM exception identity is based on tag identity — the tag that threw
    /// the exception is what matches in catch clauses. We use the core
    /// TagInstance as the identity: reference equality IS tag equality.
    /// Imported tags share the same TagInstance as the exporter (the linker
    /// wires the same object into both modules' Tags arrays).
    ///
    /// This type bridges WASM's structured exception model to the CLR's native
    /// try/catch. CLR handles unwinding; catches filter by tag reference.
    ///
    /// Spec: exception instance is {tag, fields}. throw constructs the instance
    /// and initiates unwinding. try_table catch clauses match on tag and push
    /// fields as the block's labeled values.
    /// </summary>
    public class WasmException : Exception
    {
        /// <summary>Tag identity. Reference equality → tag equality.</summary>
        public TagInstance Tag { get; }

        /// <summary>Exception field values (the payload).</summary>
        public Value[] Fields { get; }

        public WasmException(TagInstance tag, Value[] fields)
            : base("wasm exception")
        {
            Tag = tag;
            Fields = fields;
        }
    }
}
