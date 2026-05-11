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
        /// WASM-side call-stack snapshot at the moment this trap was
        /// constructed. Null when the trap site didn't pass an
        /// <see cref="ExecContext"/> (the cheap path) — most existing
        /// sites still do, and migration is incremental. Pass C's
        /// formatter reads this when present and falls back to the
        /// trap's <see cref="Exception.Message"/> + .NET stack trace
        /// when null.
        /// </summary>
        public WasmStackFrame[]? WasmFrames { get; }

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