using System;
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
    public class InstMemoryLoad : InstructionBase
    {
        public InstMemoryLoad(ValType type, BitWidth width) =>
            (Type, WidthN) = (type, width);

        public override ByteCode Op => Type switch
        {
            ValType.I32 => WidthN switch
            {
                BitWidth.U32 => OpCode.I32Load, //0x28
                BitWidth.S8 => OpCode.I32Load8S, //0x2C
                BitWidth.U8 => OpCode.I32Load8U, //0x2D
                BitWidth.S16 => OpCode.I32Load16S, //0x2E
                BitWidth.U16 => OpCode.I32Load16U, //0x2F
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {WidthN}")
            },
            ValType.I64 => WidthN switch
            {
                BitWidth.U64 => OpCode.I64Load, //0x29
                BitWidth.S8 => OpCode.I64Load8S, //0x30
                BitWidth.U8 => OpCode.I64Load8U, //0x31
                BitWidth.S16 => OpCode.I64Load16S, //0x32
                BitWidth.U16 => OpCode.I64Load16U, //0x33
                BitWidth.S32 => OpCode.I64Load32S, //0x34
                BitWidth.U32 => OpCode.I64Load32U, //0x35
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {WidthN}")
            },
            ValType.F32 => OpCode.F32Load, //0x2A
            ValType.F64 => OpCode.F64Load, //0x2B
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type}"),
        };

        private ValType Type { get; }
        private BitWidth WidthN { get; }

        private MemArg M { get; set; }

        /// <summary>
        /// @Spec 3.3.7.1. t.load
        /// @Spec 3.3.7.2. t.loadN_sx
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0),
                 $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.Align <= WidthN.ByteSize(),
                    $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.Align} <= {WidthN}/8");

            context.OpStack.PopI32();
            context.OpStack.PushType(Type);
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
            int i = context.OpStack.PopI32();
            //8.
            int ea = i + (int)M.Offset;
            //9.
            // We set the Width in the InstructionFactory
            // Floating point width will always match their type
            //10.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{WidthN.ByteSize()} out of bounds ({mem.Data.Length}).");
            //11.
            // var bs = mem.Data[ea..WidthN.ByteSize()];
            var bs = mem.Data.AsSpan(ea, WidthN.ByteSize());
            //12,13,14
            switch (Type)
            {
                case ValType.F32:
                {
                    float cF32 = BitConverter.ToSingle(bs);
                    context.OpStack.PushF32(cF32);
                    break;
                }
                case ValType.F64:
                {
                    double cF64 = BitConverter.ToDouble(bs);
                    context.OpStack.PushF64(cF64);
                    break;
                }
                default:
                    switch (WidthN)
                    {
                        case BitWidth.S8:
                            int cS8 = (sbyte)bs[0];
                            context.OpStack.PushValue(new Value(Type, cS8));
                            break;
                        case BitWidth.S16:
                            int cS16 = BitConverter.ToInt16(bs);
                            context.OpStack.PushValue(new Value(Type, cS16));
                            break;
                        case BitWidth.S32:
                            int cS32 = BitConverter.ToInt32(bs);
                            context.OpStack.PushValue(new Value(Type, cS32));
                            break;
                        case BitWidth.U8:
                            uint cU8 = bs[0];
                            context.OpStack.PushValue(new Value(Type, cU8));
                            break;
                        case BitWidth.U16:
                            uint cU16 = BitConverter.ToUInt16(bs);
                            context.OpStack.PushValue(new Value(Type, cU16));
                            break;
                        case BitWidth.U32:
                            uint cU32 = BitConverter.ToUInt32(bs);
                            context.OpStack.PushValue(new Value(Type, cU32));
                            break;
                        case BitWidth.U64:
                            ulong cU64 = BitConverter.ToUInt64(bs);
                            context.OpStack.PushValue(new Value(Type, cU64));
                            break;
                        default: throw new ArgumentOutOfRangeException();
                    }

                    break;
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

    public class InstMemoryStore : InstructionBase
    {
        public InstMemoryStore(ValType type, BitWidth width) =>
            (Type, WidthN) = (type, width);

        public override ByteCode Op => Type switch
        {
            ValType.I32 => WidthN switch
            {
                BitWidth.U8 => OpCode.I32Store8,
                BitWidth.U16 => OpCode.I32Store16,
                BitWidth.U32 => OpCode.I32Store,
                _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type} {WidthN}")
            },
            ValType.I64 => WidthN switch
            {
                BitWidth.U8 => OpCode.I64Store8,
                BitWidth.U16 => OpCode.I64Store16,
                BitWidth.U32 => OpCode.I64Store32,
                BitWidth.U64 => OpCode.I64Store,
                _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type} {WidthN}")
            },
            ValType.F32 => OpCode.F32Store,
            ValType.F64 => OpCode.F64Store,
            _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type}"),
        };

        private ValType Type { get; }
        private BitWidth WidthN { get; }
        private MemArg M { get; set; }

        public IInstruction Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        /// <summary>
        /// @Spec 3.3.7.3. t.store
        /// @Spec 3.3.7.4. t.storeN
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains((MemIdx)0),
                 $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.Align <= WidthN.ByteSize(),
                
                    $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.Align} <= {WidthN}/8");

            //Pop parameters from right to left
            context.OpStack.PopType(Type);
            context.OpStack.PopI32();
        }

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
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
            context.Assert( context.OpStack.Peek().Type == Type,
                 $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            var c = context.OpStack.PopType(Type);
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                 $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            int i = context.OpStack.PopI32();
            //10.
            int ea = i + (int)M.Offset;
            //11.
            // We set the Width in the InstructionFactory
            // Floating point width will always match their type
            //12.
            if (ea + WidthN.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan(ea, WidthN.ByteSize());
            switch (WidthN)
            {
                case BitWidth.S8:
                case BitWidth.U8:
                    byte cU8 = (byte)(0xFF & c.Int32);
                    bs[0] = cU8;
                    break;
                case BitWidth.S16:
                case BitWidth.U16:
                    byte[] cU16 = BitConverter.GetBytes((ushort)(0xFFFF & c.Int32));
                    cU16.CopyTo(bs);
                    break;
                case BitWidth.S32:
                case BitWidth.U32:
                    byte[] cU32 = BitConverter.GetBytes((uint)c.Int32);
                    cU32.CopyTo(bs);
                    break;
                case BitWidth.U64:
                    byte[] cU64 = BitConverter.GetBytes((ulong)c.Int64);
                    cU64.CopyTo(bs);
                    break;
            }
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
                    var storeValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthN)} (;>{storeValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthN)}";
        }
    }

    public struct MemArg
    {
        public uint Offset;
        public uint Align;

        public MemArg(uint align, uint offset)
        {
            Align = align;
            Offset = offset;
        }

        public static MemArg Parse(BinaryReader reader) => new()
        {
            Align = (uint)(1 << (int)reader.ReadLeb128_u32()),
            Offset = reader.ReadLeb128_u32(),
        };

        public string ToWat(BitWidth naturalAlign)
        {
            var offset = Offset != 0 ? $" offset={Offset}" : "";
            var align = Align != naturalAlign.ByteSize() ? $" align={Align}" : "";
            return $"{offset}{align}";
        }
    }

    public enum BitWidth : sbyte
    {
        S8 = -8,
        S16 = -16,
        S32 = -32,

        U8 = 8,
        U16 = 16,
        U32 = 32,
        U64 = 64,
    }

    public static class BitWidthHelpers
    {
        public static int ByteSize(this BitWidth width) =>
            width switch
            {
                BitWidth.S8 => 1,
                BitWidth.S16 => 2,
                BitWidth.S32 => 4,
                BitWidth.U8 => 1,
                BitWidth.U16 => 2,
                BitWidth.U32 => 4,
                BitWidth.U64 => 8,
                _ => (byte)width / 8
            };

        public static bool IsSigned(this BitWidth width) =>
            width switch
            {
                BitWidth.S8 => true,
                BitWidth.S16 => true,
                BitWidth.S32 => true,
                BitWidth.U8 => false,
                BitWidth.U16 => false,
                BitWidth.U32 => false,
                BitWidth.U64 => false,
                _ => false
            };
    }
}