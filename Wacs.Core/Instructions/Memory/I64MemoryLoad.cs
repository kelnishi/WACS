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
    public class InstI64Load : InstMemoryLoad, INodeComputer<uint, ulong>
    {
        public InstI64Load() : base(ValType.I64, BitWidth.U64, OpCode.I64Load) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            ulong value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public ulong FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
#if NET8_0_OR_GREATER
            return MemoryMarshal.AsRef<ulong>(bs);
#else
            return MemoryMarshal.Read<ulong>(bs);
#endif       
        }

        public Func<ExecContext, uint, ulong> GetFunc => FetchFromMemory;
    }
    
    public class InstI64Load8S : InstMemoryLoad, INodeComputer<uint, long>
    {
        public InstI64Load8S() : base(ValType.I64, BitWidth.S8, OpCode.I64Load8S) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            long value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public long FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
            
            int cS8 = (sbyte)bs[0];
            return cS8;
        }

        public Func<ExecContext, uint, long> GetFunc => FetchFromMemory;
    }
    
    public class InstI64Load8U : InstMemoryLoad, INodeComputer<uint, ulong>
    {
        public InstI64Load8U() : base(ValType.I64, BitWidth.U8, OpCode.I64Load8U) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            ulong value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public ulong FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
            
            uint cU8 = bs[0];
            return cU8;
        }

        public Func<ExecContext, uint, ulong> GetFunc => FetchFromMemory;
    }
    
    public class InstI64Load16S : InstMemoryLoad, INodeComputer<uint, long>
    {
        public InstI64Load16S() : base(ValType.I64, BitWidth.S16, OpCode.I64Load16S) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            long value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public long FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
#if NET8_0_OR_GREATER
            return MemoryMarshal.AsRef<short>(bs);
#else
            return MemoryMarshal.Read<short>(bs);
#endif
        }

        public Func<ExecContext, uint, long> GetFunc => FetchFromMemory;
    }
    
    public class InstI64Load16U : InstMemoryLoad, INodeComputer<uint, ulong>
    {
        public InstI64Load16U() : base(ValType.I64, BitWidth.U16, OpCode.I64Load16U) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            ulong value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public ulong FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);

#if NET8_0_OR_GREATER
            return MemoryMarshal.AsRef<ushort>(bs);
#else
            return MemoryMarshal.Read<ushort>(bs);
#endif
        }

        public Func<ExecContext, uint, ulong> GetFunc => FetchFromMemory;
    }
    
    public class InstI64Load32S : InstMemoryLoad, INodeComputer<uint, long>
    {
        public InstI64Load32S() : base(ValType.I64, BitWidth.S32, OpCode.I64Load16S) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            long value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public long FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
#if NET8_0_OR_GREATER
            return MemoryMarshal.AsRef<int>(bs);
#else
            return MemoryMarshal.Read<int>(bs);
#endif
        }

        public Func<ExecContext, uint, long> GetFunc => FetchFromMemory;
    }
    
    public class InstI64Load32U : InstMemoryLoad, INodeComputer<uint, ulong>
    {
        public InstI64Load32U() : base(ValType.I64, BitWidth.U32, OpCode.I64Load16U) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            uint offset = context.OpStack.PopU32();
            ulong value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }
        
        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public ulong FetchFromMemory(ExecContext context, uint offset)
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
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
#if NET8_0_OR_GREATER
            return MemoryMarshal.AsRef<uint>(bs);
#else
            return MemoryMarshal.Read<uint>(bs);
#endif
        }

        public Func<ExecContext, uint, ulong> GetFunc => FetchFromMemory;
    }
}