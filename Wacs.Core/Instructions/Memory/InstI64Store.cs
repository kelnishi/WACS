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
using System.Runtime.InteropServices;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Memory
{
    public class InstI64Store : InstMemoryStore, INodeConsumer<long, ulong>
    {
        public InstI64Store() : base(ValType.I64, BitWidth.U64, OpCode.I64Store) { }

        public Action<ExecContext, long, ulong> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            ulong c = context.OpStack.PopU64();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopAddr();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, ulong cU64)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (ea < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
#if NET8_0_OR_GREATER
            MemoryMarshal.Write(bs, in cU64);
#else
            MemoryMarshal.Write(bs, ref cU64);
#endif
        }
    }
    
    public class InstI64Store8 : InstMemoryStore, INodeConsumer<long, ulong>
    {
        public InstI64Store8() : base(ValType.I64, BitWidth.U8, OpCode.I64Store8) { }

        public Action<ExecContext, long, ulong> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            ulong c = context.OpStack.PopU64();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopAddr();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, ulong cU64)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (ea < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            mem.Data[(int)ea] = (byte)(0xFF & cU64);
        }
    }
    
    public class InstI64Store16 : InstMemoryStore, INodeConsumer<long, ulong>
    {
        public InstI64Store16() : base(ValType.I64, BitWidth.U16, OpCode.I64Store16) { }

        public Action<ExecContext, long, ulong> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            ulong c = context.OpStack.PopU64();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopAddr();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, ulong cU64)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (ea < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
            ushort cI16 = (ushort)cU64;
#if NET8_0_OR_GREATER
            MemoryMarshal.Write(bs, in cI16); // Assume you can change to 'in'
#else
            MemoryMarshal.Write(bs, ref cI16);
#endif
        }
    }
    
    public class InstI64Store32 : InstMemoryStore, INodeConsumer<long, ulong>
    {
        public InstI64Store32() : base(ValType.I64, BitWidth.U32, OpCode.I64Store32) { }

        public Action<ExecContext, long, ulong> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            ulong c = context.OpStack.PopU64();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopAddr();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, ulong cU64)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (ea < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
            uint cI32 = (uint)cU64;
#if NET8_0_OR_GREATER
            MemoryMarshal.Write(bs, in cI32); // Assume you can change to 'in'
#else
            MemoryMarshal.Write(bs, ref cI32);
#endif
            
        }
    }
}