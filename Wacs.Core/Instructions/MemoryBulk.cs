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

using System.IO;
using Wacs.Core.Instructions.Memory;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.6 Memory Instructions
namespace Wacs.Core.Instructions
{
    //0x3F
    public class InstMemorySize : InstructionBase
    {
        private MemIdx M;
        public override ByteCode Op => OpCode.MemorySize;

        /// <summary>
        /// @Spec 3.3.7.10. memory.size
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(),M);
            var mem = context.Mems[M];
            var at = mem.Limits.AddressType;
            context.OpStack.PushType(at.ToValType());
        }

        // @Spec 4.4.7.8. memory.size
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M),
                $"Instruction {Op.GetMnemonic()} failed. Memory {M} was not in the Context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Memory address {a} was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            var type = mem.Type.Limits.AddressType;
            context.OpStack.PushValue(new Value(type, mem.Size));
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            M = (MemIdx)reader.ReadByte();
            return this;
        }
    }

    //0x40
    public class InstMemoryGrow : InstructionBase
    {
        private MemIdx M;
        public override ByteCode Op => OpCode.MemoryGrow;

        /// <summary>
        /// @Spec 3.3.7.11. memory.grow
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(),M);
            var mem = context.Mems[M];
            var at = mem.Limits.AddressType.ToValType();
            context.OpStack.PopType(at);;
            context.OpStack.PushType(at);
        }

        // @Spec 4.4.7.9. memory.grow
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M),
                $"Instruction {Op.GetMnemonic()} failed. Memory {M} was not in the Context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Memory address {a} was not in the Store.");
            //5.
            var mem = context.Store[a];
            var type = mem.Type.Limits.AddressType.ToValType();
            
            //6.
            long sz = mem.Size;
            //7.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //8.
            long n = context.OpStack.PopAddr();
            //9.
            const int err = -1;
            //10,11 TODO: implement optional constraints on memory.grow
            if (mem.Grow(n))
            {
                switch (type)
                {
                    case ValType.I32:
                        context.OpStack.PushU32((uint)sz);
                        break;
                    case ValType.I64:
                        context.OpStack.PushI64(sz);
                        break;
                }
            }
            else
            {
                switch (type)
                {
                    case ValType.I32:
                        context.OpStack.PushI32(err);
                        break;
                    case ValType.I64:
                        context.OpStack.PushI64(err);
                        break;
                }
            }
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            M = (MemIdx)reader.ReadByte();
            return this;
        }
    }

    //0xFC_08
    public class InstMemoryInit : InstructionBase
    {
        private DataIdx X;
        private MemIdx Y;
        
        public override ByteCode Op => ExtCode.MemoryInit;

        /// <summary>
        /// @Spec 3.3.7.14. memory.init
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(Y),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(),Y);

            context.Assert(context.Datas.Contains(X),
                "Instruction {0} failed with invalid context data {1}.",Op.GetMnemonic(), X);

            var mem = context.Mems[Y];
            var at = mem.Limits.AddressType.ToValType();
            
            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopType(at);
        }

        // @Spec 4.4.7.12. memory.init x
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(Y),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {Y} did not exist in the context.");
            //3.
            var ma = context.Frame.Module.MemAddrs[Y];
            //4.
            context.Assert( context.Store.Contains(ma),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {Y} was not in the Store.");
            //5.
            var mem = context.Store[ma];
            var at = mem.Type.Limits.AddressType;
            
            //6.
            context.Assert( context.Frame.Module.DataAddrs.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. Address for Data {X} did not exist in the context.");
            //7.
            var da = context.Frame.Module.DataAddrs[X];
            //8.
            context.Assert( context.Store.Contains(da),
                $"Instruction {Op.GetMnemonic()} failed. Address for Data {X} was not in the Store.");
            //9.
            var data = context.Store[da];
            
            //10.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //11.
            long n = (uint)context.OpStack.PopI32();
            //12.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //13.
            long s = (uint)context.OpStack.PopI32();
            //14.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //15.
            long d = context.OpStack.PopAddr();

            if (s + n > data.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Data underflow.");
            
            if (d + n > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory overflow.");
                
            //Tail recursive call alternative loop, inline direct memory set
            while (true)
            {
                if (n == 0)
                    return;

                //Set memory direct
                mem.Data[(int)d] = (byte)(0xFF & data.Data[s]);
                
                d += 1L;
                s += 1L;
                n -= 1L;
            }
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (DataIdx)reader.ReadLeb128_u32();
            Y = (MemIdx)reader.ReadByte();
            return this;
        }

        public InstructionBase Immediate(DataIdx x, MemIdx y)
        {
            X = x;
            Y = y;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    //0xFC_09
    public class InstDataDrop : InstructionBase
    {
        private DataIdx X;
        public override ByteCode Op => ExtCode.DataDrop;

        /// <summary>
        /// @Spec 3.3.7.15. data.drop
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Datas.Contains(X),
                "Instruction {0} failed with invalid context data {1}.",Op.GetMnemonic(),X);
        }

        // @Spec 4.4.7.13
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.DataAddrs.Contains(X),
                $"Instruction {Op.GetMnemonic()} failed. Address for Data {X} did not exist in the context.");
            //3.
            var a = context.Frame.Module.DataAddrs[X];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Data {X} was not in the Store.");
            //5.
            context.Store.DropData(a);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (DataIdx)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(DataIdx x)
        {
            X = x;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    //0xFC_0A
    public class InstMemoryCopy : InstructionBase
    {
        private MemIdx DstX;
        private MemIdx SrcY;
        public override ByteCode Op => ExtCode.MemoryCopy;

        /// <summary>
        /// @Spec 3.3.7.13. memory.copy
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(SrcY),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(),SrcY);
            context.Assert(context.Mems.Contains(DstX),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(), DstX);

            var memSrcY = context.Mems[SrcY];
            var memDstX = context.Mems[DstX];
            var atS = memSrcY.Limits.AddressType;
            var atD = memDstX.Limits.AddressType;
            var atN = atS.Min(atD);
            
            context.OpStack.PopType(atN.ToValType());
            context.OpStack.PopType(atD.ToValType());
            context.OpStack.PopType(atS.ToValType());
        }

        // @Spec 4.4.7.11. memory.copy
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(SrcY),
                
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {SrcY} did not exist in the context.");
            //3.
            context.Assert( context.Frame.Module.MemAddrs.Contains(DstX),
                
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {DstX} did not exist in the context.");
            //4.
            var sa = context.Frame.Module.MemAddrs[SrcY];
            //5.
            var da = context.Frame.Module.MemAddrs[DstX];
            //6.
            context.Assert( context.Store.Contains(da),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {SrcY} was not in the Store.");
            //7.
            context.Assert( context.Store.Contains(da),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {DstX} was not in the Store.");
            //8.
            var memSrc = context.Store[sa];
            //9.
            var memDst = context.Store[da];
            
            //10.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //11.
            long n = context.OpStack.PopAddr();
            //12.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //13.
            long s = context.OpStack.PopAddr();
            //14.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //15.
            long d = context.OpStack.PopAddr();
            //16.
            if (s+n > memSrc.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Source memory overflow.");
            if (d+n > memDst.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Destination memory overflow.");
            
            //Tail recursive call alternative loop, inline load/store
            while (true)
            {
                if (n == 0)
                    return;
                if (d <= s)
                {
                    memDst.Data[(int)d] = memSrc.Data[(int)s];
                    d += 1L;
                    s += 1L;
                }
                else
                {
                    memDst.Data[(int)(d+n-1L)] = memSrc.Data[(int)(s+n-1L)];
                }
                n -= 1L;
            }
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            DstX = (MemIdx)reader.ReadByte();
            SrcY = (MemIdx)reader.ReadByte();
            return this;
        }
    }

    //0xFC_0B
    public class InstMemoryFill : InstructionBase
    {
        private MemIdx X;
        public override ByteCode Op => ExtCode.MemoryFill;

        /// <summary>
        /// @Spec 3.3.7.12. memory.fill
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(X),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(),X);
            
            var mem = context.Mems[X];
            var at = mem.Limits.AddressType;
            context.OpStack.PopType(at.ToValType());
            context.OpStack.PopI32();
            context.OpStack.PopType(at.ToValType());
        }

        // @Spec 4.4.7.10. memory.fill
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(X),
                
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {X} did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[X];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory {X} was not in the Store.");
            //5.
            var mem = context.Store[a];
            var at = mem.Type.Limits.AddressType;

            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            long n = (uint)context.OpStack.PopI32();
            //8,9. YOLO
            var val = context.OpStack.PopAny();
            uint cU32 = val;
            //10.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //11.
            long d = context.OpStack.PopAddr();
            //12.
            if (d + n > mem.Data.Length)
                throw new TrapException("Instruction memory.fill failed. Buffer overflow");
            
            //Tail recursive call alternative loop, inline store
            while (true)
            {
                if (n == 0)
                    return;
                
                mem.Data[(int)d] = (byte)(0xFF & cU32);
                d += 1L;
                n -= 1L;
            }
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (MemIdx)reader.ReadByte();
            return this;
        }
    }
}