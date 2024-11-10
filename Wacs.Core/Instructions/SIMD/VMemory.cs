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

        private BitWidth WidthT { get; }
        private int CountN { get; }
        private MemArg M { get; set; }

        /// <summary>
        /// @Spec 3.3.7.5. v128.loadNxM_sx memarg
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0),
                 $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.AlignBytes <= WidthT.ByteSize() * CountN,
                    $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.AlignBytes} <= {WidthT}/8");

            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.1. t.load and t.loadN_sx
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains((MemIdx)0),
                 $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[(MemIdx)0];
            //4.
            context.Assert( context.Store.Contains(a),
                 $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                 $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            uint i = context.OpStack.PopI32();
            //8.
            long ea = (long)i + (long)M.Offset;
            //9.
            int mn = M.AlignBytes * CountN;
            if (ea + mn > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{mn} out of bounds ({mem.Data.Length}).");
            //10.
            var bs = mem.Data.AsSpan((int)ea, mn);
            //11,12,13,14
            int m = M.AlignBytes;
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
        public InstMemoryLoadSplat(BitWidth width) => WidthN = width;

        public override ByteCode Op => WidthN switch
        {
            BitWidth.U8 => SimdCode.V128Load8Splat,
            BitWidth.U16 => SimdCode.V128Load16Splat,
            BitWidth.U32 => SimdCode.V128Load32Splat,
            BitWidth.U64 => SimdCode.V128Load64Splat,
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {WidthN}"),
        };

        private BitWidth WidthN { get; }

        private MemArg M { get; set; }

        /// <summary>
        /// @Spec 3.3.7.6. v128.loadN_splat
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0),
                $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.Align <= WidthN.ByteSize(),
                $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.Align} <= {BitWidth.V128}/8");

            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.3. v128.loadN_splat
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains((MemIdx)0),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[(MemIdx)0];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            uint i = context.OpStack.PopI32();
            //8.
            long ea = (long)i + (long)M.Offset;
            //9.
            // We set the Width in the InstructionFactory
            // Floating point width will always match their type
            //10.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthN.ByteSize()} out of bounds ({mem.Data.Length}).");
            //11.
            // var bs = mem.Data[ea..WidthN.ByteSize()];
            var bs = mem.Data.AsSpan((int)ea, WidthN.ByteSize());
            //12,13,14
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
    
    public class InstMemoryLoadLane : InstructionBase
    {
        public InstMemoryLoadLane(BitWidth width) => WidthN = width;

        public override ByteCode Op => WidthN switch
        {
            BitWidth.U8 => SimdCode.V128Load8Lane,
            BitWidth.U16 => SimdCode.V128Load16Lane,
            BitWidth.U32 => SimdCode.V128Load32Lane,
            BitWidth.U64 => SimdCode.V128Load64Lane,
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {WidthN}"),
        };

        private BitWidth WidthN { get; }

        private MemArg M { get; set; }

        private LaneIdx L { get; set; }

        /// <summary>
        /// @Spec 3.3.7.1. t.load
        /// @Spec 3.3.7.2. t.loadN_sx
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0),
                $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.Align <= BitWidth.V128.ByteSize(),
                $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.Align} <= {BitWidth.V128}/8");
            context.Assert(L < 16/WidthN.ByteSize(),
                $"Instruction {Op.GetMnemonic()} failed with invalid laneidx {L} <= {16/WidthN.ByteSize()}");
            
            context.OpStack.PopV128();
            context.OpStack.PopI32();
            context.OpStack.PushV128();
        }

        // @Spec 4.4.7.1. t.load and t.loadN_sx
        public override void Execute(ExecContext context)
        {
            MV128 value = (V128)context.OpStack.PopV128();
            
            //2.
            context.Assert( context.Frame.Module.MemAddrs.Contains((MemIdx)0),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 did not exist in the context.");
            //3.
            var a = context.Frame.Module.MemAddrs[(MemIdx)0];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Instruction {Op.GetMnemonic()} failed. Address for Memory 0 was not in the Store.");
            //5.
            var mem = context.Store[a];
            //6.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            uint i = context.OpStack.PopI32();
            //8.
            long ea = (long)i + (long)M.Offset;
            //9.
            // We set the Width in the InstructionFactory
            // Floating point width will always match their type
            //10.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthN.ByteSize()} out of bounds ({mem.Data.Length}).");
            //11.
            // var bs = mem.Data[ea..WidthN.ByteSize()];
            var bs = mem.Data.AsSpan((int)ea, WidthN.ByteSize());
            //12,13,14
            switch (WidthN)
            {
                case BitWidth.U8:
                    
                    switch (L)
                    {
                        case 0: value.B8x16_0 = bs[0]; break;
                    }
                    context.OpStack.PushV128(value);
                    break;
                case BitWidth.U16:
                    ushort cU16 = BitConverter.ToUInt16(bs);
                    context.OpStack.PushValue(new V128(cU16,cU16,cU16,cU16,cU16,cU16,cU16,cU16));
                    break;
                case BitWidth.U32:
                    uint cU32 = BitConverter.ToUInt32(bs);
                    context.OpStack.PushValue(new V128(cU32,cU32,cU32,cU32));
                    break;
                case BitWidth.U64:
                    ulong cU64 = BitConverter.ToUInt64(bs);
                    context.OpStack.PushValue(new V128(cU64, cU64));
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public IInstruction Immediate(MemArg m, LaneIdx l)
        {
            M = m;
            L = l;
            return this;
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            L = reader.ReadByte();
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
        

}