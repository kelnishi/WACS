// Copyright 2026 Kelvin Nishikawa
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
using System.Collections.Generic;
using Wacs.Core.Runtime.Exceptions;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Thrown by the <c>suspend</c> opcode to unwind the interpreter
    /// stack back to the nearest <c>resume</c> / <c>resume_throw</c>
    /// / <c>switch</c> frame whose installed handlers include the
    /// triggering tag. The unwind mirrors how
    /// <see cref="UnhandledWasmException"/> propagates out of a
    /// thrown exception until <c>try_table</c>'s catch table picks
    /// it up — same control-stack walk, distinct exception type so
    /// the two paths don't cross-catch.
    /// </summary>
    public sealed class SuspensionException : WasmRuntimeException
    {
        public readonly TagAddr Tag;
        public readonly Stack<Value> Payload;

        public SuspensionException(TagAddr tag, Stack<Value> payload)
            : base($"suspend (tag {tag})")
        {
            Tag = tag;
            Payload = payload;
        }
    }
}
