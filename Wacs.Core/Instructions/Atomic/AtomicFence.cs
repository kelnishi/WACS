// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Threading;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Atomic
{
    /// <summary>
    /// <c>atomic.fence</c> — sequentially-consistent memory barrier.
    /// Binary encoding carries a single reserved <c>0x00</c> byte (threads
    /// proposal §5.4). No memarg, no operands, no result.
    /// </summary>
    public sealed class InstAtomicFence : InstructionBase
    {
        public InstAtomicFence() : base((ByteCode)AtomCode.AtomicFence) {}

        public int CalculateSize() => 1;
        public int LinkStackDiff => StackDiff;

        public override InstructionBase Parse(BinaryReader reader)
        {
            byte reserved = reader.ReadByte();
            if (reserved != 0x00)
                throw new InvalidDataException(
                    $"atomic.fence expects reserved zero byte, got 0x{reserved:X2}.");
            return this;
        }

        public override void Validate(IWasmValidationContext context) { /* no-op */ }

        public override void Execute(ExecContext context) => Interlocked.MemoryBarrier();
    }
}
