// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Atomic
{
    /// <summary>
    /// <c>memory.atomic.notify</c> — wake up to <c>count</c> waiters at
    /// <c>(memory, addr)</c>. Pushes the number of waiters actually woken.
    /// Stack: <c>(addr, count) → woken</c>.
    /// </summary>
    public sealed class InstMemoryAtomicNotify : InstAtomicMemoryOp
    {
        public InstMemoryAtomicNotify()
            : base((ByteCode)AtomCode.MemoryAtomicNotify, widthBytes: 4, stackDiff: -1)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI32();           // count
            context.OpStack.PopInt();           // addr
            context.OpStack.PushI32();          // woken
        }

        public override void Execute(ExecContext context)
        {
            int count = context.OpStack.PopI32();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            int woken = context.ConcurrencyPolicy.Notify(CachedMem, ea, count);
            context.OpStack.PushI32(woken);
        }
    }

    /// <summary>
    /// <c>memory.atomic.wait32</c> — block until the i32 cell at
    /// <c>(memory, addr)</c> is notified or the timeout elapses.
    /// Stack: <c>(addr, expected, timeoutNs) → result</c>.
    /// Result: 0 = ok, 1 = timed-out, 2 = not-equal.
    /// </summary>
    public sealed class InstMemoryAtomicWait32 : InstAtomicMemoryOp
    {
        public InstMemoryAtomicWait32()
            : base((ByteCode)AtomCode.MemoryAtomicWait32, widthBytes: 4, stackDiff: -2)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI64();           // timeoutNs
            context.OpStack.PopI32();           // expected
            context.OpStack.PopInt();           // addr
            context.OpStack.PushI32();          // result
        }

        public override void Execute(ExecContext context)
        {
            long timeoutNs = context.OpStack.PopI64();
            int expected = context.OpStack.PopI32();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            int result = context.ConcurrencyPolicy.Wait32(CachedMem, ea, expected, timeoutNs);
            context.OpStack.PushI32(result);
        }
    }

    /// <summary>
    /// <c>memory.atomic.wait64</c> — i64 variant of wait32.
    /// Stack: <c>(addr, expected, timeoutNs) → result</c>.
    /// </summary>
    public sealed class InstMemoryAtomicWait64 : InstAtomicMemoryOp
    {
        public InstMemoryAtomicWait64()
            : base((ByteCode)AtomCode.MemoryAtomicWait64, widthBytes: 8, stackDiff: -2)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI64();           // timeoutNs
            context.OpStack.PopI64();           // expected
            context.OpStack.PopInt();           // addr
            context.OpStack.PushI32();          // result
        }

        public override void Execute(ExecContext context)
        {
            long timeoutNs = context.OpStack.PopI64();
            long expected = context.OpStack.PopI64();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            int result = context.ConcurrencyPolicy.Wait64(CachedMem, ea, expected, timeoutNs);
            context.OpStack.PushI32(result);
        }
    }
}
