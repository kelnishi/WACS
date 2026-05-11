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

namespace Wacs.Core.Runtime.Types
{
    public class TrapException : Exception
    {
        /// <summary>
        /// WASM-side call-stack snapshot. Set either at construction
        /// (sites that pass an <see cref="ExecContext"/> directly) or
        /// retroactively by the dispatch loop's outer catch in
        /// <c>WasmRuntime.ProcessThreadAsync</c> — that catch
        /// enriches null-framed traps in-place so the great majority
        /// of unmigrated throw sites still surface a useful trace.
        /// </summary>
        public WasmStackFrame[]? WasmFrames { get; internal set; }

        public TrapException(string message) : base(message)
        {
        }

        /// <summary>
        /// Construct with an attached WASM-side stack snapshot.
        /// Throw sites that have an <see cref="ExecContext"/> in
        /// scope (i.e. almost all of them) can opt in by calling
        /// <c>ctx.SnapshotCallStack(this)</c> at throw time and
        /// passing the result here.
        /// </summary>
        public TrapException(string message, WasmStackFrame[] wasmFrames)
            : base(message)
        {
            WasmFrames = wasmFrames;
        }
    }

    public class OutOfBoundsTableAccessException : TrapException
    {
        public OutOfBoundsTableAccessException(string message) : base(message)
        {
        }

        public OutOfBoundsTableAccessException(string message, WasmStackFrame[] wasmFrames)
            : base(message, wasmFrames)
        {
        }
    }
}