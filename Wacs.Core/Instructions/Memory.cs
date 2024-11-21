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
using System.Runtime.InteropServices;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Validation;

// 5.4.6 Memory Instructions
namespace Wacs.Core.Instructions
{
    public class InstMemoryLoad : InstructionBase
    {
        private readonly ValType Type;
        private readonly BitWidth WidthT;

        private MemArg M;

        public InstMemoryLoad(ValType type, BitWidth width) =>
            (Type, WidthT) = (type, width);

        public override ByteCode Op => Type switch
        {
            ValType.I32 => WidthT switch
            {
                BitWidth.U32 => OpCode.I32Load, //0x28
                BitWidth.S8 => OpCode.I32Load8S, //0x2C
                BitWidth.U8 => OpCode.I32Load8U, //0x2D
                BitWidth.S16 => OpCode.I32Load16S, //0x2E
                BitWidth.U16 => OpCode.I32Load16U, //0x2F
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {WidthT}")
            },
            ValType.I64 => WidthT switch
            {
                BitWidth.U64 => OpCode.I64Load, //0x29
                BitWidth.S8 => OpCode.I64Load8S, //0x30
                BitWidth.U8 => OpCode.I64Load8U, //0x31
                BitWidth.S16 => OpCode.I64Load16S, //0x32
                BitWidth.U16 => OpCode.I64Load16U, //0x33
                BitWidth.S32 => OpCode.I64Load32S, //0x34
                BitWidth.U32 => OpCode.I64Load32U, //0x35
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {WidthT}")
            },
            ValType.V128 => WidthT switch
            {
                BitWidth.S8 => SimdCode.V128Load8x8S,
                BitWidth.U8 => SimdCode.V128Load8x8U,
                BitWidth.S16 => SimdCode.V128Load16x4S,
                BitWidth.U16 => SimdCode.V128Load16x4U,
                BitWidth.S32 => SimdCode.V128Load32x2S,
                BitWidth.U32 => SimdCode.V128Load32x2U,
                BitWidth.V128 => SimdCode.V128Load,
                _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type} {WidthT}")
            },
            ValType.F32 => OpCode.F32Load, //0x2A
            ValType.F64 => OpCode.F64Load, //0x2B
            _ => throw new InvalidDataException($"InstMemoryLoad instruction is malformed: {Type}"),
        };

        /// <summary>
        /// @Spec 3.3.7.1. t.load
        /// @Spec 3.3.7.2. t.loadN_sx
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                 $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.Align.LinearSize() <= WidthT.ByteSize(),
                    $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.Align.LinearSize()} <= {WidthT}/8");

            context.OpStack.PopI32();
            context.OpStack.PushType(Type);
        }

        // @Spec 4.4.7.1. t.load and t.loadN_sx
        public override int Execute(ExecContext context)
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
            int n = WidthT.ByteSize();
            //10.
            if (ea + n > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer {ea}+{n} out of bounds ({mem.Data.Length}).");
            //11.
            var bs = mem.Data.AsSpan((int)ea, n);
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
                case ValType.V128:
                {
                    context.OpStack.PushV128(new V128(bs));
                    break;
                }
                default:
                    switch (WidthT)
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
            return 1;
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

    public class InstMemoryStore : InstructionBase
    {
        private readonly BitWidth TWidth;

        private readonly ValType Type;
        private MemArg M;

        public InstMemoryStore(ValType type, BitWidth width) =>
            (Type, TWidth) = (type, width);

        public override ByteCode Op => Type switch
        {
            ValType.I32 => TWidth switch
            {
                BitWidth.U8 => OpCode.I32Store8,
                BitWidth.U16 => OpCode.I32Store16,
                BitWidth.U32 => OpCode.I32Store,
                _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type} {TWidth}")
            },
            ValType.I64 => TWidth switch
            {
                BitWidth.U8 => OpCode.I64Store8,
                BitWidth.U16 => OpCode.I64Store16,
                BitWidth.U32 => OpCode.I64Store32,
                BitWidth.U64 => OpCode.I64Store,
                _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type} {TWidth}")
            },
            ValType.F32 => OpCode.F32Store,
            ValType.F64 => OpCode.F64Store,
            ValType.V128 => SimdCode.V128Store,
            _ => throw new InvalidDataException($"InstMemoryStore instruction is malformed: {Type}"),
        };

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
            context.Assert(context.Mems.Contains(M.M),
                 $"Instruction {Op.GetMnemonic()} failed with invalid context memory 0.");
            context.Assert(M.Align.LinearSize() <= TWidth.ByteSize(),
                
                    $"Instruction {Op.GetMnemonic()} failed with invalid alignment {M.Align.LinearSize()} <= {TWidth}/8");

            //Pop parameters from right to left
            context.OpStack.PopType(Type);
            context.OpStack.PopI32();
        }

        // @Spec 4.4.7.6. t.store
        // @Spec 4.4.7.6. t.storeN
        public override int Execute(ExecContext context)
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
            context.Assert( context.OpStack.Peek().Type == Type,
                 $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //7.
            var c = context.OpStack.PopType(Type);
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                 $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopU32();
            //10.
            long ea = i + M.Offset;
            //11.
            // We set the Width in the InstructionFactory
            // Floating point width will always match their type
            //12.
            if (ea + TWidth.ByteSize() > mem.Data.Length)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Memory pointer out of bounds.");
            //13,14,15
            Span<byte> bs = mem.Data.AsSpan((int)ea, TWidth.ByteSize());
            switch (TWidth)
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
                case BitWidth.V128:
                    V128 cV128 = c.V128;
                    var cData = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref cV128, 1));
                    cData.CopyTo(bs);
                    break;
            }
            return 1;
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
                    return $"{base.RenderText(context)}{M.ToWat(TWidth)} (;>{storeValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(TWidth)}";
        }
    }

    

    
}