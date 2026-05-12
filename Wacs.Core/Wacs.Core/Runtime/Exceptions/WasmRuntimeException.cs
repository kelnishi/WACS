// Copyright 2024 Kelvin Nishikawa
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

namespace Wacs.Core.Runtime.Exceptions
{
    public class WasmRuntimeException : Exception
    {
        /// <summary>
        /// WASM-side call-stack snapshot — populated retroactively
        /// by the dispatch loop's outer catch when the exception was
        /// thrown from inside an instruction's <c>Execute</c>. Null
        /// for setup-time failures (module instantiation, host
        /// binding, type-system errors at construction) where no
        /// call stack exists yet.
        /// </summary>
        public WasmStackFrame[]? WasmFrames { get; internal set; }

        public WasmRuntimeException(string message) : base(message) { }

        public WasmRuntimeException(string message, WasmStackFrame[] wasmFrames)
            : base(message)
        {
            WasmFrames = wasmFrames;
        }
    }
}