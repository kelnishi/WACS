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
    public class InstF32Load : InstMemoryLoad, INodeComputer<long, float>
    {
        public InstF32Load() : base(ValType.F32, BitWidth.U32, OpCode.F32Load) {}

        public Func<ExecContext, long, float> GetFunc => FetchFromMemory;

        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopAddr();
            float value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }

        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public float FetchFromMemory(ExecContext context, long offset)
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
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
            
            return MemoryMarshal.Read<float>(bs);
        }
    }
    
    
    public class InstF64Load : InstMemoryLoad, INodeComputer<long, double>
    {
        public InstF64Load() : base(ValType.F64, BitWidth.U64, OpCode.F64Load) {}

        public Func<ExecContext, long, double> GetFunc => FetchFromMemory;

        public override void Execute(ExecContext context)
        {
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            long offset = context.OpStack.PopAddr();
            double value = FetchFromMemory(context, offset);
            context.OpStack.PushValue(value);
        }

        //@Spec 4.4.7.1. t.load and t.loadN_sx
        public double FetchFromMemory(ExecContext context, long offset)
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
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthTByteSize} out of bounds ({mem.Data.Length}).");
            var bs = new ReadOnlySpan<byte>(mem.Data, (int)ea, WidthTByteSize);
            
            return MemoryMarshal.Read<double>(bs);
        }
    }
}