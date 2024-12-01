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

namespace Wacs.Core.Instructions.Memory
{
    public class InstI32Load : InstMemoryLoad, INodeComputer<uint, uint>
    {
        public InstI32Load() : base(ValType.I32, BitWidth.U32, OpCode.I32Load) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            uint value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public uint FetchFromMemory(ExecContext context, uint offset)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];
            long i = offset;
            long ea = (long)i + (long)M.Offset;
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
            return MemoryMarshal.Read<uint>(bs);
        }

        public Func<ExecContext, uint, uint> GetFunc => FetchFromMemory;
    }
    
    public class InstI32Load8S : InstMemoryLoad, INodeComputer<uint, int>
    {
        public InstI32Load8S() : base(ValType.I32, BitWidth.S8, OpCode.I32Load8S) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            int value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public int FetchFromMemory(ExecContext context, uint offset)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];
            long i = offset;
            long ea = (long)i + (long)M.Offset;
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
            int cS8 = (sbyte)bs[0];
            return cS8;
        }

        public Func<ExecContext, uint, int> GetFunc => FetchFromMemory;
    }
    
    public class InstI32Load8U : InstMemoryLoad, INodeComputer<uint, uint>
    {
        public InstI32Load8U() : base(ValType.I32, BitWidth.U8, OpCode.I32Load8U) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            uint value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public uint FetchFromMemory(ExecContext context, uint offset)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];
            long i = offset;
            long ea = (long)i + (long)M.Offset;
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = mem.Data.AsSpan((int)ea, WidthTByteSize);
            
            
            uint cU8 = bs[0];
            return cU8;
        }

        public Func<ExecContext, uint, uint> GetFunc => FetchFromMemory;
    }
    
    public class InstI32Load16S : InstMemoryLoad, INodeComputer<uint, int>
    {
        public InstI32Load16S() : base(ValType.I32, BitWidth.S16, OpCode.I32Load16S) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            int value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public int FetchFromMemory(ExecContext context, uint offset)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];
            long i = offset;
            long ea = (long)i + (long)M.Offset;
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = mem.Data.AsSpan((int)ea, WidthTByteSize);

            return MemoryMarshal.Read<short>(bs);
        }

        public Func<ExecContext, uint, int> GetFunc => FetchFromMemory;
    }
    
    public class InstI32Load16U : InstMemoryLoad, INodeComputer<uint, uint>
    {
        public InstI32Load16U() : base(ValType.I32, BitWidth.U16, OpCode.I32Load16U) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            uint value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public uint FetchFromMemory(ExecContext context, uint offset)
        {
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            var a = context.Frame.Module.MemAddrs[M.M];
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            var mem = context.Store[a];
            long i = offset;
            long ea = (long)i + (long)M.Offset;
            if (ea + WidthTByteSize > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = mem.Data.AsSpan((int)ea, WidthTByteSize);

            return MemoryMarshal.Read<ushort>(bs);
        }

        public Func<ExecContext, uint, uint> GetFunc => FetchFromMemory;
    }
}