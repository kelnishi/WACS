// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

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
    public class InstI32Store : InstMemoryStore, INodeConsumer<long, uint>
    {
        public InstI32Store() : base(ValType.I32, BitWidth.U32, OpCode.I32Store) { }

        public Action<ExecContext, long, uint> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint c = context.OpStack.PopU32();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopInt();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, uint cU32)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (offset < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory offset {offset} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
#if NET8_0
            MemoryMarshal.Write(bs, in cU32);
#else
            MemoryMarshal.Write(bs, ref cU32);
#endif
        }
    }
    
    public class InstI32Store8 : InstMemoryStore, INodeConsumer<long, uint>
    {
        public InstI32Store8() : base(ValType.I32, BitWidth.U8, OpCode.I32Store8) { }

        public Action<ExecContext, long, uint> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint c = context.OpStack.PopU32();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopInt();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, uint cU32)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (offset < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory offset {offset} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            // Span<byte> bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            mem.Data[(int)ea] = (byte)(0xFF & cU32);
        }
    }
    
    public class InstI32Store16 : InstMemoryStore, INodeConsumer<long, uint>
    {
        public InstI32Store16() : base(ValType.I32, BitWidth.U16, OpCode.I32Store16) { }

        public Action<ExecContext, long, uint> GetFunc => SetMemoryValue;

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().Type == Type,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint c = context.OpStack.PopU32();
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopInt();
            
            SetMemoryValue(context, offset, c);
        }

        public void SetMemoryValue(ExecContext context, long offset, uint cU32)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];

            long ea = offset + M.Offset;
            if (offset < 0)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory offset {offset} out of bounds.");
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
            ushort cI16 = (ushort)cU32;
#if NET8_0
            MemoryMarshal.Write(bs, in cI16); // Assume you can change to 'in'
#else
            MemoryMarshal.Write(bs, ref cI16);
#endif
        }
    }
}