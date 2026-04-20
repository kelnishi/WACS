// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// A saved caller-context record that the iterative switch runtime pushes onto
    /// <see cref="ExecContext._switchCallStack"/> on every WASM <c>call</c>. It holds
    /// exactly the per-frame state that <c>GeneratedDispatcher.Run</c>'s mutable
    /// locals (<c>code</c>, <c>handlers</c>, <c>_pc</c>, <c>ctx.Frame</c>) will need
    /// to resume the caller once the callee's execution returns through the
    /// natural <c>pc &gt;= code.Length</c> loop-top check.
    ///
    /// <para>Layout: 5 reference/value-type fields = 32 B on 64-bit platforms with
    /// pointer-size alignment. Pooling isn't needed — the stack itself is
    /// pre-allocated in <see cref="ExecContext"/>'s ctor.</para>
    ///
    /// <para>What's NOT stored here: <c>_stackCount</c>. The callee's effect on the
    /// OpStack is already observable via <c>_opStack.Count</c> when we pop a frame,
    /// so the caller just re-syncs <c>_stackCount = _opStack.Count</c>. Saving a
    /// caller-time value would be wrong — the stack has grown by arity results
    /// minus popped args, and the post-call resync is how we pick that up.</para>
    /// </summary>
    internal readonly struct SwitchCallFrame
    {
        /// <summary>The caller's bytecode to resume executing on pop.</summary>
        public readonly byte[] Code;

        /// <summary>The caller's handler table, for exception-unwind lookups.</summary>
        public readonly HandlerEntry[] Handlers;

        /// <summary>The caller's <see cref="Runtime.Types.Frame"/>, restored to
        /// <c>ctx.Frame</c> so locals/module-addresses belong to the caller again.</summary>
        public readonly Frame WasmFrame;

        /// <summary>The caller's pc to resume at — the opcode immediately after the
        /// <c>call</c> (or <c>call_indirect</c> / <c>call_ref</c>) that pushed us.</summary>
        public readonly int ResumePc;

        /// <summary>The callee's rented <c>Value[]</c> locals array, so we can
        /// <see cref="System.Buffers.ArrayPool{T}.Return"/> it on pop. (The
        /// callee's <c>Frame.Locals</c> is a <see cref="System.Memory{T}"/> view over
        /// this array; keeping the raw reference saves a <c>MemoryMarshal.TryGetArray</c>
        /// on the hot exit path.)</summary>
        public readonly Value[] CalleeRentedLocals;

        public SwitchCallFrame(byte[] code, HandlerEntry[] handlers, Frame wasmFrame,
                                int resumePc, Value[] calleeRentedLocals)
        {
            Code = code;
            Handlers = handlers;
            WasmFrame = wasmFrame;
            ResumePc = resumePc;
            CalleeRentedLocals = calleeRentedLocals;
        }
    }
}
