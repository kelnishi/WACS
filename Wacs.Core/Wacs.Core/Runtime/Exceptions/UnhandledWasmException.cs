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

namespace Wacs.Core.Runtime.Exceptions
{
    /// <summary>
    /// Thrown from the runtime when a wasm exception is unhandled.
    /// </summary>
    public class UnhandledWasmException : Exception
    {
        /// <summary>
        /// WASM-side call-stack snapshot at the throw site, when the
        /// runtime captured it. Null when the path that constructed
        /// this exception didn't have an <see cref="ExecContext"/>
        /// in scope. Pass C's formatter reads this for verbose
        /// stack traces.
        /// </summary>
        public WasmStackFrame[]? WasmFrames { get; }

        public UnhandledWasmException(string message) : base(message) { }

        public UnhandledWasmException(string message, WasmStackFrame[] wasmFrames)
            : base(message)
        {
            WasmFrames = wasmFrames;
        }
    }
}