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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
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
            context.OpStack.PushI32();
        }

        // @Spec 4.4.7.8. memory.size
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M),
                 $"Instruction memory.grow failed. Memory {M} was not in the Context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M];
            //4.
            context.Assert( context.Store.Contains(a),
                 $"Instruction memory.grow failed. Memory address {a} was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            uint sz = (uint)mem.Size;
            //7.
            context.OpStack.PushU32(sz);
        }

        public override IInstruction Parse(BinaryReader reader)
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
            context.OpStack.PopI32();
            context.OpStack.PushI32();
        }

        // @Spec 4.4.7.9. memory.grow
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M),
                 $"Instruction memory.grow failed. Memory {M} was not in the Context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M];
            //4.
            context.Assert( context.Store.Contains(a),
                 $"Instruction memory.grow failed. Memory address {a} was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            uint sz = (uint)mem.Size;
            //7.
            context.Assert( context.OpStack.Peek().IsI32,
                 $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //8.
            uint n = context.OpStack.PopU32();
            //9.
            const int err = -1;
            //10,11 TODO: implement optional constraints on memory.grow
            if (mem.Grow(n))
            {
                context.OpStack.PushU32(sz);
            }
            else
            {
                context.OpStack.PushI32(err);
            }
        }

        public override IInstruction Parse(BinaryReader reader)
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

            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
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

            //Tail recursive call alternative loop
            while (true)
            {
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
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
                //15.
                long d = (uint)context.OpStack.PopI32();
                //16.
                if (s + n > data.Data.Length)
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Data underflow.");
                if (d + n > mem.Data.Length)
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory overflow.");
                //17.
                if (n == 0)
                    return;
                //18.
                byte b = data.Data[s];
                //19.
                context.OpStack.PushU32((uint)d);
                //20.
                context.OpStack.PushI32(b);
                //21.
                context.InstructionFactory.CreateInstruction<InstMemoryStore>(OpCode.I32Store8).Immediate(new MemArg(0, 0, Y))
                    .Execute(context);
                //22.
                long check = d + 1L;
                context.Assert( check < Constants.TwoTo32,
                     $"Instruction {Op.GetMnemonic()} failed. Memory overflow.");
                //23.
                context.OpStack.PushU32((uint)(d + 1L));
                //24.
                check = s + 1L;
                context.Assert( check < Constants.TwoTo32,
                     $"Instruction {Op.GetMnemonic()} failed. Data overflow.");
                //25.
                context.OpStack.PushU32((uint)(s + 1L));
                context.OpStack.PushU32((uint)(n - 1L));
            }
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            X = (DataIdx)reader.ReadLeb128_u32();
            Y = (MemIdx)reader.ReadByte();
            
            return this;
        }

        public IInstruction Immediate(DataIdx x)
        {
            X = x;
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

        public override IInstruction Parse(BinaryReader reader)
        {
            X = (DataIdx)reader.ReadLeb128_u32();
            return this;
        }

        public IInstruction Immediate(DataIdx x)
        {
            X = x;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {X.Value}";
    }

    //0xFC_0A
    public class InstMemoryCopy : InstructionBase
    {
        private MemIdx DstY;
        private MemIdx SrcX;
        public override ByteCode Op => ExtCode.MemoryCopy;

        /// <summary>
        /// @Spec 3.3.7.13. memory.copy
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(SrcX),
                 "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(),SrcX);
            context.Assert(context.Mems.Contains(DstY),
                "Instruction {0} failed with invalid context memory {1}.",Op.GetMnemonic(), DstY);

            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
        }

        // @Spec 4.4.7.11. memory.copy
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(SrcX),
                
                    $"Instruction {Op.GetMnemonic()} failed. Address for Memory {SrcX} did not exist in the context.");
            context.Assert( context.Frame.Module.MemAddrs.Contains(DstY),
                
                    $"Instruction {Op.GetMnemonic()} failed. Address for Memory {DstY} did not exist in the context.");
            //3.
            var maX = context.Frame.Module.MemAddrs[SrcX];
            var maY = context.Frame.Module.MemAddrs[DstY];
            //4.
            context.Assert( context.Store.Contains(maX),
                 $"Instruction {Op.GetMnemonic()} failed. Address for Memory {SrcX} was not in the Store.");
            context.Assert( context.Store.Contains(maY),
                 $"Instruction {Op.GetMnemonic()} failed. Address for Memory {DstY} was not in the Store.");
            //5.
            var memX = context.Store[maX];
            var memY = context.Store[maY];

            //Tail recursive call alternative loop
            while (true)
            {
                //6.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
                //7.
                long n = (uint)context.OpStack.PopI32();
                //8.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
                //9.
                long s = (uint)context.OpStack.PopI32();
                //10.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
                //11.
                long d = (uint)context.OpStack.PopI32();
                //12
                long check = s + n;
                if (check > memX.Data.Length)
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Source memory overflow.");
                check = d + n;
                if (check > memY.Data.Length)
                    throw new TrapException(
                        $"Instruction {Op.GetMnemonic()} failed. Destination memory overflow.");
                //13.
                if (n == 0)
                    return;
                //14.
                if (d <= s)
                {
                    context.OpStack.PushU32((uint)d);
                    context.OpStack.PushU32((uint)s);
                    context.InstructionFactory.CreateInstruction<InstMemoryLoad>(OpCode.I32Load8U).Immediate(new MemArg(0, 0, SrcX))
                        .Execute(context);
                    context.InstructionFactory.CreateInstruction<InstMemoryStore>(OpCode.I32Store8).Immediate(new MemArg(0, 0, DstY))
                        .Execute(context);
                    check = d + 1L;
                    context.Assert( check < Constants.TwoTo32,
                         $"Instruction {Op.GetMnemonic()} failed. Destination memory overflow.");
                    context.OpStack.PushU32((uint)(d + 1L));
                    check = s + 1L;
                    context.Assert( check < Constants.TwoTo32,
                         $"Instruction {Op.GetMnemonic()} failed. Source memory overflow.");
                    context.OpStack.PushU32((uint)(s + 1L));
                }
                //15.
                else
                {
                    check = d + n - 1L;
                    context.Assert( check < Constants.TwoTo32,
                         $"Instruction {Op.GetMnemonic()} failed. Destination memory overflow.");
                    context.OpStack.PushU32((uint)(d + n - 1));
                    check = s + n - 1L;
                    context.Assert( check < Constants.TwoTo32,
                         $"Instruction {Op.GetMnemonic()} failed. Source memory overflow.");
                    context.OpStack.PushU32((uint)(s + n - 1));
                    context.InstructionFactory.CreateInstruction<InstMemoryLoad>(OpCode.I32Load8U).Immediate(new MemArg(0, 0, SrcX))
                        .Execute(context);
                    context.InstructionFactory.CreateInstruction<InstMemoryStore>(OpCode.I32Store8).Immediate(new MemArg(0, 0, DstY))
                        .Execute(context);
                    context.OpStack.PushU32((uint)d);
                    context.OpStack.PushU32((uint)s);
                }

                //16.
                context.OpStack.PushU32((uint)n - 1);
                //17.
            }
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            SrcX = (MemIdx)reader.ReadByte();
            DstY = (MemIdx)reader.ReadByte();
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

            context.OpStack.PopI32();
            context.OpStack.PopI32();
            context.OpStack.PopI32();
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

            //Tail recursive call alternative loop
            while (true)
            {
                //6.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
                //7.
                long n = (uint)context.OpStack.PopI32();
                //8,9. YOLO
                var val = context.OpStack.PopAny();
                //10.
                context.Assert( context.OpStack.Peek().IsI32,
                     $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
                //11.
                long d = (uint)context.OpStack.PopI32();
                //12.
                if (d + n > mem.Data.Length)
                    throw new TrapException("Instruction memory.fill failed. Buffer overflow");
                //13.
                if (n == 0)
                    return;
                //14.
                context.OpStack.PushU32((uint)d);
                //15.
                context.OpStack.PushValue(val);
                //16.
                
                context.InstructionFactory.CreateInstruction<InstMemoryStore>(OpCode.I32Store8).Immediate(new MemArg(0, 0, X))
                    .Execute(context);
                //17.
                long check = d + 1L;
                context.Assert( check < Constants.TwoTo32,
                     $"Instruction memory.fill failed. Buffer overflow");
                //18.
                context.OpStack.PushU32((uint)(d + 1));
                //19.
                context.OpStack.PushValue(val);
                //20.
                context.OpStack.PushU32((uint)(n - 1));
                //21.
            }
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            X = (MemIdx)reader.ReadByte();
            return this;
        }
    }
}