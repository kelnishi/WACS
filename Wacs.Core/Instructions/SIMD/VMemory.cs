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
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Validation;
using LaneIdx = System.Byte;

namespace Wacs.Core.Instructions.SIMD
{
    public class InstMemoryLoadMxN : InstructionBase
    {
        private readonly int CountN;

        private readonly BitWidth WidthT;
        private MemArg M;

        public InstMemoryLoadMxN(BitWidth width, int count) =>
            (WidthT, CountN) = (width, count);

        public override ByteCode Op => CountN switch
        {
            8 => WidthT switch
            {
                BitWidth.S8 => SimdCode.V128Load8x8S,
                BitWidth.U8 => SimdCode.V128Load8x8U,
                _ => throw new InvalidDataException($"InstMemoryLoadMxN instruction is malformed: {WidthT}x{CountN}"),
            },
            4 => WidthT switch
            {
                BitWidth.S16 => SimdCode.V128Load16x4S,
                BitWidth.U16 => SimdCode.V128Load16x4U,
                _ => throw new InvalidDataException($"InstMemoryLoadMxN instruction is malformed: {WidthT}x{CountN}"),
            },
            2 => WidthT switch
            {
                BitWidth.S32 => SimdCode.V128Load32x2S,
                BitWidth.U32 => SimdCode.V128Load32x2U,
                _ => throw new InvalidDataException($"InstMemoryLoadMxN instruction is malformed: {WidthT}x{CountN}"),
            },
            _ => throw new InvalidDataException($"InstMemoryLoadMxN instruction is malformed: {WidthT}x{CountN}"),
        };

        /// <summary>
        /// @Spec 3.3.7.5. v128.loadNxM_sx memarg
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                "Instruction {0} failed with invalid context memory 0.",Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= WidthT.ByteSize() * CountN,
                "Instruction {0} failed with invalid alignment {1} <= {2}/8",Op.GetMnemonic(),M.Align.LinearSize(),WidthT);

            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.2. v128.loadMxN_sx memarg
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M.M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            long i = context.OpStack.PopU32();
            //8.
            long ea = (long)i + (long)M.Offset;
            //9.
            int mn = WidthT.ByteSize() * CountN;
            if (ea + mn > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{mn} out of bounds ({mem.Data.Length}).");
            //10.
            var bs = mem.Data.AsSpan((int)ea, mn);
            //11,12,13,14
            int m = WidthT.ByteSize();
            MV128 c = new MV128();
            for (int k = 0; k < CountN; ++k)
            {
                int km = k * m;
                int kmEnd = km + WidthT.ByteSize();
                var cell = bs[km..kmEnd];
                switch (WidthT)
                {
                    case BitWidth.S8: c[(short)k] = (sbyte)cell[0]; break;
                    case BitWidth.U8: c[(short)k] = cell[0]; break;
                    case BitWidth.S16: c[(int)k] = BitConverter.ToInt16(cell); break;
                    case BitWidth.U16: c[(int)k] = BitConverter.ToUInt16(cell); break;
                    case BitWidth.S32: c[(long)k] = BitConverter.ToInt32(cell); break;
                    case BitWidth.U32: c[(long)k] = BitConverter.ToUInt32(cell); break;
                }
            }
            //15.
            context.OpStack.PushV128((V128)c);
        }

        public IInstruction Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var loadedValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthT)} (;>{loadedValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthT)}";
        }
    }
    
    public class InstMemoryLoadSplat : InstructionBase
    {
        private readonly BitWidth WidthN;

        private MemArg M;
        public InstMemoryLoadSplat(BitWidth width) => WidthN = width;

        public override ByteCode Op => WidthN switch
        {
            BitWidth.U8 => SimdCode.V128Load8Splat,
            BitWidth.U16 => SimdCode.V128Load16Splat,
            BitWidth.U32 => SimdCode.V128Load32Splat,
            BitWidth.U64 => SimdCode.V128Load64Splat,
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {WidthN}"),
        };

        /// <summary>
        /// @Spec 3.3.7.6. v128.loadN_splat
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                "Instruction {0} failed with invalid context memory 0.",Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= WidthN.ByteSize(),
                "Instruction {0} failed with invalid alignment {1} <= {2}/8",Op.GetMnemonic(),M.Align,BitWidth.V128);

            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.3. v128.loadN_splat
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M.M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            long i = context.OpStack.PopU32();
            //8.
            long ea = (long)i + (long)M.Offset;
            //9.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthN.ByteSize()} out of bounds ({mem.Data.Length}).");
            //10.
            var bs = mem.Data.AsSpan((int)ea, WidthN.ByteSize());
            //11,12,13,14
            switch (WidthN)
            {
                case BitWidth.U8:
                    byte cU8 = bs[0];
                    context.OpStack.PushV128(new V128(cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8,cU8));
                    break;
                case BitWidth.U16:
                    ushort cU16 = BitConverter.ToUInt16(bs);
                    context.OpStack.PushV128(new V128(cU16,cU16,cU16,cU16,cU16,cU16,cU16,cU16));
                    break;
                case BitWidth.U32:
                    uint cU32 = BitConverter.ToUInt32(bs);
                    context.OpStack.PushV128(new V128(cU32,cU32,cU32,cU32));
                    break;
                case BitWidth.U64:
                    ulong cU64 = BitConverter.ToUInt64(bs);
                    context.OpStack.PushV128(new V128(cU64, cU64));
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public IInstruction Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var loadedValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthN)} (;>{loadedValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthN)}";
        }
    }
    
    public class InstMemoryLoadZero : InstructionBase
    {
        private readonly BitWidth WidthN;

        private MemArg M;
        public InstMemoryLoadZero(BitWidth width) => WidthN = width;

        public override ByteCode Op => WidthN switch
        {
            BitWidth.U8 => SimdCode.V128Load8Lane,
            BitWidth.U16 => SimdCode.V128Load16Lane,
            BitWidth.U32 => SimdCode.V128Load32Lane,
            BitWidth.U64 => SimdCode.V128Load64Lane,
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {WidthN}"),
        };

        /// <summary>
        /// @Spec 3.3.7.7. v128.loadN_zero memarg
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                "Instruction {0} failed with invalid context memory 0.",Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= BitWidth.V128.ByteSize(),
                "Instruction {0} failed with invalid alignment {1} <= {2}/8",Op.GetMnemonic(),M.Align.LinearSize(),BitWidth.V128);

            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.7. v128.loadN_zero memarg
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M.M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            long i = context.OpStack.PopU32();
            //8.
            long ea = (long)i + (long)M.Offset;
            //9.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthN.ByteSize()} out of bounds ({mem.Data.Length}).");
            //10.
            var bs = mem.Data.AsSpan((int)ea, WidthN.ByteSize());
            //11,12,13
            switch (WidthN)
            {
                case BitWidth.U32:
                    uint cU32 = BitConverter.ToUInt32(bs);
                    context.OpStack.PushValue(new V128(cU32,0,0,0));
                    break;
                case BitWidth.U64:
                    ulong cU64 = BitConverter.ToUInt64(bs);
                    context.OpStack.PushValue(new V128(cU64, 0));
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public IInstruction Immediate(MemArg m, LaneIdx l)
        {
            M = m;
            return this;
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var loadedValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthN)} (;>{loadedValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthN)}";
        }
    }
    
    public class InstMemoryLoadLane : InstructionBase
    {
        private readonly BitWidth WidthN;

        private MemArg M;

        private LaneIdx X;
        public InstMemoryLoadLane(BitWidth width) => WidthN = width;

        public override ByteCode Op => WidthN switch
        {
            BitWidth.U8 => SimdCode.V128Load8Lane,
            BitWidth.U16 => SimdCode.V128Load16Lane,
            BitWidth.U32 => SimdCode.V128Load32Lane,
            BitWidth.U64 => SimdCode.V128Load64Lane,
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {WidthN}"),
        };

        /// <summary>
        /// @Spec 3.3.7.8. v128.loadN_lane memarge laneidx
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(X < 128 / WidthN.BitSize(),
                "Instruction {0} failed with invalid laneidx {1} <= {2}", Op.GetMnemonic(), X, 128 / WidthN.BitSize());
            context.Assert(context.Mems.Contains(M.M),
                "Instruction {0} failed with invalid context memory 0.", Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= WidthN.ByteSize(),
                "Instruction {0} failed with invalid alignment {1} <= {2}/8", Op.GetMnemonic(), M.Align.LinearSize(), WidthN);
            
            context.OpStack.PopV128();
            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.5. v128.loadN_lane memarg x
        public override void Execute(ExecContext context)
        {
            
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M.M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsV128,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            MV128 value = (V128)context.OpStack.PopV128();
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopU32();
            //10.
            long ea = (long)i + (long)M.Offset;
            //11.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthN.ByteSize()} out of bounds ({mem.Data.Length}).");
            //12.
            var bs = mem.Data.AsSpan((int)ea, WidthN.ByteSize());
            //13,14,15,16
            switch (WidthN)
            {
                case BitWidth.U8: value[(byte)X] = bs[0]; break;
                case BitWidth.U16: value[(ushort)X] = BitConverter.ToUInt16(bs);  break;
                case BitWidth.U32: value[(uint)X] = BitConverter.ToUInt32(bs); break;
                case BitWidth.U64: value[(ulong)X] = BitConverter.ToUInt64(bs); break;
                default: throw new ArgumentOutOfRangeException($"Instruction {Op.GetMnemonic()} failed. Cannot convert bytes to {WidthN}.");
            }
            //17.
            context.OpStack.PushV128(value);
        }

        public IInstruction Immediate(MemArg m, LaneIdx l)
        {
            M = m;
            X = l;
            return this;
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            X = reader.ReadByte();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var loadedValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthN)} {X} (;>{loadedValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthN)} {X}";
        }
    }
        
    public class InstMemoryStoreLane : InstructionBase
    {
        private readonly BitWidth WidthN;
        private MemArg M;

        private LaneIdx X;
        public InstMemoryStoreLane(BitWidth width) => WidthN = width;

        public override ByteCode Op => WidthN switch
        {
            BitWidth.U8 =>  SimdCode.V128Store8Lane,
            BitWidth.U16 => SimdCode.V128Store16Lane,
            BitWidth.U32 => SimdCode.V128Store32Lane,
            BitWidth.U64 => SimdCode.V128Store64Lane,
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {WidthN}"),
        };

        public IInstruction Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        /// <summary>
        /// @Spec 3.3.7.9. v128.storeN_lane memarg laneidx
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(X < 128 / WidthN.BitSize(),
                "Instruction {0} failed with invalid laneidx {1} <= {2}", Op.GetMnemonic(), X, 128 / WidthN.BitSize());
            context.Assert(context.Mems.Contains(M.M),
                "Instruction {0} failed with invalid context memory 0.", Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= WidthN.ByteSize(),
                "Instruction {0} failed with invalid alignment {1} <= {2}/8", Op.GetMnemonic(), M.Align.LinearSize(), WidthN);
            context.OpStack.PopV128();
            context.OpStack.PopI32();
        }

        // @Spec 4.4.7.7. v128.storeN_lane memarg x
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains(M.M),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[M.M];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsV128,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            V128 c = context.OpStack.PopV128();
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopU32();
            //10.
            long ea = i + M.Offset;
            //11.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            
            //12,13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, 16);
            switch (WidthN)
            {
                case BitWidth.S8:
                case BitWidth.U8:
                    bs[0] = c[(byte)X];
                    break;
                case BitWidth.S16:
                case BitWidth.U16:
                    byte[] cU16 = BitConverter.GetBytes(c[(ushort)X]);
                    cU16.CopyTo(bs);
                    break;
                case BitWidth.S32:
                case BitWidth.U32:
                    byte[] cU32 = BitConverter.GetBytes(c[(uint)X]);
                    cU32.CopyTo(bs);
                    break;
                case BitWidth.U64:
                    byte[] cU64 = BitConverter.GetBytes(c[(ulong)X]);
                    cU64.CopyTo(bs);
                    break;
            }
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            X = reader.ReadByte();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var storeValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthN)} {X}(;>{storeValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthN)} {X}";
        }
    }
}