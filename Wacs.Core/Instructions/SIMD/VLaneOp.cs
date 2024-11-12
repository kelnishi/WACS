using System;
using System.IO;
using FluentValidation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;
using LaneIdx = System.Byte;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.Instructions.Numeric
{
    public class InstLaneOp : InstructionBase
    {
        private readonly ExecuteDelegate _execute;
        private readonly ValidationDelegate _validate;

        private InstLaneOp(ByteCode op, ExecuteDelegate execute, ValidationDelegate validate) =>
            (Op, _execute, _validate) = (op, execute, validate);

        public override ByteCode Op { get; }
        private LaneIdx X { get; set; }
        public static InstLaneOp I8x16ExtractLaneS() => new(SimdCode.I8x16ExtractLaneS, ExecuteI8x16ExtractLaneS, ValidateFromLane(V128Shape.I8x16));
        public static InstLaneOp I8x16ExtractLaneU() => new(SimdCode.I8x16ExtractLaneU, ExecuteI8x16ExtractLaneU, ValidateFromLane(V128Shape.I8x16));
        public static InstLaneOp I16x8ExtractLaneS() => new(SimdCode.I16x8ExtractLaneS, ExecuteI16x8ExtractLaneS, ValidateFromLane(V128Shape.I16x8));
        public static InstLaneOp I16x8ExtractLaneU() => new(SimdCode.I16x8ExtractLaneU, ExecuteI16x8ExtractLaneU, ValidateFromLane(V128Shape.I16x8));
        public static InstLaneOp I32x4ExtractLane()  => new(SimdCode.I32x4ExtractLane, ExecuteI32x4ExtractLane, ValidateFromLane(V128Shape.I32x4));
        public static InstLaneOp I64x2ExtractLane()  => new(SimdCode.I64x2ExtractLane, ExecuteI64x2ExtractLane, ValidateFromLane(V128Shape.I64x2));
        public static InstLaneOp F32x4ExtractLane()  => new(SimdCode.F32x4ExtractLane, ExecuteF32x4ExtractLane, ValidateFromLane(V128Shape.F32x4));
        public static InstLaneOp F64x2ExtractLane()  => new(SimdCode.F64x2ExtractLane, ExecuteF64x2ExtractLane, ValidateFromLane(V128Shape.F64x2));

        public static InstLaneOp I8x16ReplaceLane() => new(SimdCode.I8x16ReplaceLane, ExecuteI8x16ReplaceLane, ValidateToLane(V128Shape.I8x16));
        public static InstLaneOp I16x8ReplaceLane() => new(SimdCode.I16x8ReplaceLane, ExecuteI16x8ReplaceLane, ValidateToLane(V128Shape.I16x8));
        public static InstLaneOp I32x4ReplaceLane() => new(SimdCode.I32x4ReplaceLane, ExecuteI32x4ReplaceLane, ValidateToLane(V128Shape.I32x4));
        public static InstLaneOp I64x2ReplaceLane() => new(SimdCode.I64x2ReplaceLane, ExecuteI64x2ReplaceLane, ValidateToLane(V128Shape.I64x2));
        public static InstLaneOp F32x4ReplaceLane() => new(SimdCode.F32x4ReplaceLane, ExecuteF32x4ReplaceLane, ValidateToLane(V128Shape.F32x4));
        public static InstLaneOp F64x2ReplaceLane() => new(SimdCode.F64x2ReplaceLane, ExecuteF64x2ReplaceLane, ValidateToLane(V128Shape.F64x2));

        public static void ExecuteI8x16ExtractLaneS(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            int result = (int)c[(sbyte)laneidx];
            context.OpStack.PushI32(result);
        }

        public static void ExecuteI8x16ExtractLaneU(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            uint result = (uint)c[(byte)laneidx];
            context.OpStack.PushI32((int)result);
        }

        public static void ExecuteI16x8ExtractLaneS(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            int result = (int)c[(short)laneidx];
            context.OpStack.PushI32(result);
        }

        public static void ExecuteI16x8ExtractLaneU(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            uint result = (uint)c[(ushort)laneidx];
            context.OpStack.PushI32((int)result);
        }

        public static void ExecuteI32x4ExtractLane(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            int result = (int)c[(int)laneidx];
            context.OpStack.PushI32(result);
        }

        public static void ExecuteI64x2ExtractLane(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            long result = (long)c[(long)laneidx];
            context.OpStack.PushI64(result);
        }

        public static void ExecuteF32x4ExtractLane(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            float result = (float)c[(float)laneidx];
            context.OpStack.PushF32(result);
        }

        public static void ExecuteF64x2ExtractLane(ExecContext context, LaneIdx laneidx)
        {
            V128 c = context.OpStack.PopV128();
            double result = (double)c[(double)laneidx];
            context.OpStack.PushF64(result);
        }

        public static void ExecuteI8x16ReplaceLane(ExecContext context, LaneIdx laneidx)
        {
            byte v = (byte)(uint)context.OpStack.PopI32();
            MV128 c = (V128)context.OpStack.PopV128();
            c[(byte)laneidx] = v;
            context.OpStack.PushV128(c);
        }

        public static void ExecuteI16x8ReplaceLane(ExecContext context, LaneIdx laneidx)
        {
            ushort v = (ushort)(uint)context.OpStack.PopI32();
            MV128 c = (V128)context.OpStack.PopV128();
            c[(ushort)laneidx] = v;
            context.OpStack.PushV128(c);
        }

        public static void ExecuteI32x4ReplaceLane(ExecContext context, LaneIdx laneidx)
        {
            int v = context.OpStack.PopI32();
            MV128 c = (V128)context.OpStack.PopV128();
            c[(int)laneidx] = v;
            context.OpStack.PushV128(c);
        }

        public static void ExecuteI64x2ReplaceLane(ExecContext context, LaneIdx laneidx)
        {
            long v = context.OpStack.PopI64();
            MV128 c = (V128)context.OpStack.PopV128();
            c[(long)laneidx] = v;
            context.OpStack.PushV128(c);
        }

        public static void ExecuteF32x4ReplaceLane(ExecContext context, LaneIdx laneidx)
        {
            float v = context.OpStack.PopF32();
            MV128 c = (V128)context.OpStack.PopV128();
            c[(float)laneidx] = v;
            context.OpStack.PushV128(c);
        }

        public static void ExecuteF64x2ReplaceLane(ExecContext context, LaneIdx laneidx)
        {
            double v = context.OpStack.PopF64();
            MV128 c = (V128)context.OpStack.PopV128();
            c[(double)laneidx] = v;
            context.OpStack.PushV128(c);
        }

        public override void Validate(IWasmValidationContext context) => _validate(context, Op, X);
        public override void Execute(ExecContext context) => _execute(context, X);

        public override IInstruction Parse(BinaryReader reader)
        {
            X = reader.ReadByte();
            return this;
        }


        // @Spec 3.3.3.9. shape.extract_lane_sx
        private static ValidationDelegate ValidateFromLane(V128Shape shape) =>
            (context, op, l) =>
            {
                context.OpStack.PopV128();
                switch (shape)
                {
                    case V128Shape.I8x16:context.OpStack.PushI32(); break;
                    case V128Shape.I16x8: context.OpStack.PushI32(); break;
                    case V128Shape.I32x4: context.OpStack.PushI32(); break;
                    case V128Shape.I64x2: context.OpStack.PushI64(); break;
                    case V128Shape.F32x4: context.OpStack.PushF32(); break;
                    case V128Shape.F64x2: context.OpStack.PushF64(); break;
                    default: throw new ValidationException($"Instruction {op.GetMnemonic()} was invalid. Unsupported lane shape {shape}");
                }
            };

        // @Spec 3.3.3.10. shape.replace_lane laneidx
        private static ValidationDelegate ValidateToLane(V128Shape shape) =>
            (context, op, l) =>
            {
                switch (shape)
                {
                    case V128Shape.I8x16: 
                        context.Assert(l < 16, $"Instruction {op.GetMnemonic()} was invalid. Target Lane out of bounds.");
                        context.OpStack.PopI32();
                        break;
                    case V128Shape.I16x8:
                        context.Assert(l < 8, $"Instruction {op.GetMnemonic()} was invalid. Target Lane out of bounds.");
                        context.OpStack.PopI32(); 
                        break;
                    case V128Shape.I32x4:
                        context.Assert(l < 4, $"Instruction {op.GetMnemonic()} was invalid. Target Lane out of bounds.");
                        context.OpStack.PopI32(); 
                        break;
                    case V128Shape.I64x2: 
                        context.Assert(l < 2, $"Instruction {op.GetMnemonic()} was invalid. Target Lane out of bounds.");
                        context.OpStack.PopI64(); 
                        break;
                    case V128Shape.F32x4:
                        context.Assert(l < 4, $"Instruction {op.GetMnemonic()} was invalid. Target Lane out of bounds.");
                        context.OpStack.PopF32(); 
                        break;
                    case V128Shape.F64x2:
                        context.Assert(l < 2, $"Instruction {op.GetMnemonic()} was invalid. Target Lane out of bounds.");
                        context.OpStack.PopF64();
                        break;
                    default: throw new InvalidOperationException("Unsupported lane shape");
                }
                context.OpStack.PopV128();
                context.OpStack.PushV128();
            };

        private delegate void ExecuteDelegate(ExecContext context, LaneIdx laneidx);

        private delegate void ValidationDelegate(IWasmValidationContext context, ByteCode op, LaneIdx l);
    }
}