// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Atomic
{
    /// <summary>
    /// Shared base for every atomic memory op (load, store, rmw, cmpxchg).
    /// Carries the <see cref="MemArg"/>, caches the resolved
    /// <see cref="MemoryInstance"/> at link time, and enforces the two
    /// atomics-specific validation rules that differ from non-atomic
    /// memory ops:
    /// <list type="number">
    ///   <item><b>Exact natural alignment</b> — <c>memarg.align</c> must
    ///   equal the access width in bytes (threads proposal §3.4). Non-atomic
    ///   memory ops only require <c>align ≤ width</c>.</item>
    ///   <item><b>Shared memory requirement</b> — the target memory must
    ///   be declared <c>shared</c>. Strict by default; relaxable via
    ///   <see cref="RuntimeAttributes.RelaxAtomicSharedCheck"/>.</item>
    /// </list>
    /// </summary>
    public abstract class InstAtomicMemoryOp : InstructionBase, IComplexLinkBehavior
    {
        protected MemArg M;
        /// <summary>Natural byte width of this op's access (1, 2, 4, or 8).</summary>
        protected readonly int WidthBytes;
        /// <summary>Cached at link time so Execute can skip the Store lookup.</summary>
        protected MemoryInstance CachedMem = null!;

        protected InstAtomicMemoryOp(ByteCode opcode, int widthBytes, int stackDiff)
            : base(opcode, stackDiff)
        {
            WidthBytes = widthBytes;
        }

        public long MemOffset => M.Offset;
        public int MemIndex => (int)M.M.Value;
        public int AccessWidth => WidthBytes;

        public int CalculateSize() => 1;
        public int LinkStackDiff => StackDiff;

        public override InstructionBase Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            return this;
        }

        public InstructionBase Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                "Instruction {0} failed with invalid context memory {1}.",
                Op.GetMnemonic(), M.M.Value);

            // Exact alignment — threads §3.4. Non-atomic ops use ≤, which
            // hides the fact that Alignment.LinearSize() returns 0 when
            // the log2 exponent is 0 (i.e. align=1). For atomics we need
            // an exact match, so compare via the log2 form directly.
            int expectedLog2 = WidthBytes switch
            {
                1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => -1
            };
            context.Assert(M.Align.LogSize() == expectedLog2,
                "Instruction {0} failed with non-natural alignment 2^{1} (expected exactly 2^{2} = {3} bytes).",
                Op.GetMnemonic(), M.Align.LogSize(), expectedLog2, WidthBytes);

            // Shared memory requirement. Strict by default; hosts may relax
            // via RuntimeAttributes.RelaxAtomicSharedCheck for toolchains
            // that emit atomics on non-shared memories.
            if (!context.Attributes.RelaxAtomicSharedCheck)
            {
                var memType = context.Mems[M.M];
                context.Assert(memType.Limits.Shared,
                    "Instruction {0} requires a shared memory (memarg target {1} is not shared).",
                    Op.GetMnemonic(), M.M.Value);
            }

            ValidateStack(context);
        }

        /// <summary>Subclass hook: consume/produce stack slots per op family.</summary>
        protected abstract void ValidateStack(IWasmValidationContext context);

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            context.Assert(context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Memory {M.M.Value} not in the module's MemAddrs.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert(context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Memory {M.M.Value} missing from Store.");
            CachedMem = context.Store[a];

            // Enable concurrent-grow protection on this memory if the
            // host has opted into concurrent execution. Single-threaded
            // defaults never allocate the lock, so this is a no-op there.
            if (context.ConcurrencyPolicy.Mode == ConcurrencyPolicyMode.HostDefined
                && CachedMem.Type.Limits.Shared)
            {
                CachedMem.EnableConcurrentGrow();
            }

            return base.Link(context, pointer);
        }

        public override string RenderText(ExecContext? context) =>
            $"{base.RenderText(context)}{M.ToWat(NaturalAlignBitWidth())}";

        /// <summary>BitWidth form used by MemArg.ToWat to decide whether the
        /// alignment annotation should be rendered. Maps the byte width.</summary>
        private BitWidth NaturalAlignBitWidth() => WidthBytes switch
        {
            1 => BitWidth.U8,
            2 => BitWidth.U16,
            4 => BitWidth.U32,
            8 => BitWidth.U64,
            _ => BitWidth.None,
        };

        /// <summary>Bounds + alignment check. Returns the effective address.
        /// Traps on out-of-bounds or unaligned access.</summary>
        protected long CheckEa(long offset)
        {
            long ea = offset + M.Offset;
            if (ea < 0)
                throw new TrapException(
                    $"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea} out of bounds.");
            if (ea + WidthBytes > CachedMem.Data.Length)
                throw new TrapException(
                    $"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthBytes} out of bounds ({CachedMem.Data.Length}).");
            if ((ea & (WidthBytes - 1)) != 0)
                throw new TrapException(
                    $"Instruction {Op.GetMnemonic()} failed. Unaligned atomic access at {ea} (width {WidthBytes}).");
            return ea;
        }
    }

    /// <summary>
    /// Base for atomic load variants. Pops an address, pushes the loaded
    /// value. Subclasses override <see cref="DoLoad"/> to implement the
    /// actual read + zero-extension pattern.
    /// </summary>
    public abstract class InstAtomicLoad : InstAtomicMemoryOp
    {
        /// <summary>Value-type of the *result* pushed on the stack (i32 or i64).</summary>
        protected readonly ValType ResultType;

        protected InstAtomicLoad(ByteCode opcode, ValType resultType, int widthBytes)
            : base(opcode, widthBytes, 0) // pop addr (-1), push result (+1) = 0
        {
            ResultType = resultType;
        }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopInt();           // -1
            context.OpStack.PushType(ResultType); // +0
        }

        public override void Execute(ExecContext context)
        {
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            DoLoad(context, (int)ea);
        }

        /// <summary>Perform the atomic load and push onto the op stack.</summary>
        protected abstract void DoLoad(ExecContext context, int ea);
    }

    /// <summary>Base for i32 atomic store variants. Pops a 32-bit value
    /// then an address.</summary>
    public abstract class InstAtomicStore32 : InstAtomicMemoryOp
    {
        protected InstAtomicStore32(ByteCode opcode, int widthBytes)
            : base(opcode, widthBytes, -2)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI32();
            context.OpStack.PopInt();
        }

        public override void Execute(ExecContext context)
        {
            int value = context.OpStack.PopI32();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            DoStore(CachedMem, (int)ea, value);
        }

        protected abstract void DoStore(MemoryInstance mem, int ea, int value);
    }

    /// <summary>Base for i64 atomic store variants. Pops a 64-bit value
    /// then an address.</summary>
    public abstract class InstAtomicStore64 : InstAtomicMemoryOp
    {
        protected InstAtomicStore64(ByteCode opcode, int widthBytes)
            : base(opcode, widthBytes, -2)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI64();
            context.OpStack.PopInt();
        }

        public override void Execute(ExecContext context)
        {
            long value = context.OpStack.PopI64();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            DoStore(CachedMem, (int)ea, value);
        }

        protected abstract void DoStore(MemoryInstance mem, int ea, long value);
    }

    /// <summary>
    /// Base for atomic read-modify-write ops (add/sub/and/or/xor/xchg) on
    /// i32 and sub-words of i32. Pops <c>(addr, arg)</c>, pushes the
    /// original cell value (zero-extended for sub-words).
    /// </summary>
    public abstract class InstAtomicRmw32 : InstAtomicMemoryOp
    {
        protected InstAtomicRmw32(ByteCode opcode, int widthBytes)
            : base(opcode, widthBytes, -1) // pop arg (-1), pop addr (-2), push result (+1) = -1
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI32();           // -1 (arg)
            context.OpStack.PopInt();           // -2 (addr)
            context.OpStack.PushI32();          // -1 (result)
        }

        public override void Execute(ExecContext context)
        {
            int arg = context.OpStack.PopI32();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            int original = DoRmw(CachedMem, (int)ea, arg);
            context.OpStack.PushI32(original);
        }

        /// <summary>Perform the op, return the original (pre-op) value
        /// zero-extended to 32 bits.</summary>
        protected abstract int DoRmw(MemoryInstance mem, int ea, int arg);
    }

    /// <summary>Base for atomic RMW ops on i64 and sub-words of i64.</summary>
    public abstract class InstAtomicRmw64 : InstAtomicMemoryOp
    {
        protected InstAtomicRmw64(ByteCode opcode, int widthBytes)
            : base(opcode, widthBytes, -1)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI64();           // -1 (arg)
            context.OpStack.PopInt();           // -2 (addr)
            context.OpStack.PushI64();          // -1 (result)
        }

        public override void Execute(ExecContext context)
        {
            long arg = context.OpStack.PopI64();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            long original = DoRmw(CachedMem, (int)ea, arg);
            context.OpStack.PushI64(original);
        }

        protected abstract long DoRmw(MemoryInstance mem, int ea, long arg);
    }

    /// <summary>Base for atomic cmpxchg variants (i32 + subwords). Stack:
    /// <c>(addr, expected, replacement) → original</c>.</summary>
    public abstract class InstAtomicCmpxchg32 : InstAtomicMemoryOp
    {
        protected InstAtomicCmpxchg32(ByteCode opcode, int widthBytes)
            : base(opcode, widthBytes, -2) // pop 3, push 1
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI32();           // replacement
            context.OpStack.PopI32();           // expected
            context.OpStack.PopInt();           // addr
            context.OpStack.PushI32();          // result
        }

        public override void Execute(ExecContext context)
        {
            int replacement = context.OpStack.PopI32();
            int expected = context.OpStack.PopI32();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            int original = DoCmpxchg(CachedMem, (int)ea, expected, replacement);
            context.OpStack.PushI32(original);
        }

        protected abstract int DoCmpxchg(MemoryInstance mem, int ea, int expected, int replacement);
    }

    /// <summary>Base for atomic cmpxchg on i64 + subwords.</summary>
    public abstract class InstAtomicCmpxchg64 : InstAtomicMemoryOp
    {
        protected InstAtomicCmpxchg64(ByteCode opcode, int widthBytes)
            : base(opcode, widthBytes, -2)
        { }

        protected override void ValidateStack(IWasmValidationContext context)
        {
            context.OpStack.PopI64();
            context.OpStack.PopI64();
            context.OpStack.PopInt();
            context.OpStack.PushI64();
        }

        public override void Execute(ExecContext context)
        {
            long replacement = context.OpStack.PopI64();
            long expected = context.OpStack.PopI64();
            long offset = context.OpStack.PopAddr();
            long ea = CheckEa(offset);
            long original = DoCmpxchg(CachedMem, (int)ea, expected, replacement);
            context.OpStack.PushI64(original);
        }

        protected abstract long DoCmpxchg(MemoryInstance mem, int ea, long expected, long replacement);
    }
}
