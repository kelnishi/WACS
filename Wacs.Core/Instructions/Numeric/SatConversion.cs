using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32TruncSatF32S = new(OpCode.I32TruncSatF32S, ExecuteI32TruncSatF32S,
            ValidateOperands(pop: ValType.F32, push: ValType.I32));

        public static readonly NumericInst I32TruncSatF32U = new(OpCode.I32TruncSatF32U, ExecuteI32TruncSatF32U,
            ValidateOperands(pop: ValType.F32, push: ValType.I32));

        public static readonly NumericInst I32TruncSatF64S = new(OpCode.I32TruncSatF64S, ExecuteI32TruncSatF64S,
            ValidateOperands(pop: ValType.F64, push: ValType.I32));

        public static readonly NumericInst I32TruncSatF64U = new(OpCode.I32TruncSatF64U, ExecuteI32TruncSatF64U,
            ValidateOperands(pop: ValType.F64, push: ValType.I32));

        public static readonly NumericInst I64TruncSatF32S = new(OpCode.I64TruncSatF32S, ExecuteI64TruncSatF32S,
            ValidateOperands(pop: ValType.F32, push: ValType.I64));

        public static readonly NumericInst I64TruncSatF32U = new(OpCode.I64TruncSatF32U, ExecuteI64TruncSatF32U,
            ValidateOperands(pop: ValType.F32, push: ValType.I64));

        public static readonly NumericInst I64TruncSatF64S = new(OpCode.I64TruncSatF64S, ExecuteI64TruncSatF64S,
            ValidateOperands(pop: ValType.F64, push: ValType.I64));

        public static readonly NumericInst I64TruncSatF64U = new(OpCode.I64TruncSatF64U, ExecuteI64TruncSatF64U,
            ValidateOperands(pop: ValType.F64, push: ValType.I64));

        // https://github.com/WebAssembly/spec/blob/master/proposals/nontrapping-float-to-int-conversion/Overview.md
        private static void ExecuteI32TruncSatF32S(ExecContext context)
        {
            float value = context.OpStack.PopF32();
            float truncated = (float)Math.Truncate(value);
            int result = truncated switch
            {
                float.NaN => 0,
                float.PositiveInfinity => int.MaxValue,
                float.NegativeInfinity => int.MinValue,
                < int.MinValue => int.MinValue,
                > int.MaxValue => int.MaxValue,
                _ => (int)truncated
            };
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32TruncSatF32U(ExecContext context)
        {
            float value = context.OpStack.PopF32();
            float truncated = (float)Math.Truncate(value);
            uint result = truncated switch
            {
                float.NaN => 0,
                float.PositiveInfinity => uint.MaxValue,
                float.NegativeInfinity => 0,
                < 0 => 0,
                > uint.MaxValue => uint.MaxValue,
                _ => (uint)truncated
            };
            context.OpStack.PushI32((int)result);
        }

        private static void ExecuteI32TruncSatF64S(ExecContext context)
        {
            double value = context.OpStack.PopF64();
            double truncated = Math.Truncate(value);
            int result = truncated switch
            {
                double.NaN => 0,
                double.PositiveInfinity => int.MaxValue,
                double.NegativeInfinity => int.MinValue,
                < int.MinValue => int.MinValue,
                > int.MaxValue => int.MaxValue,
                _ => (int)truncated
            };
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32TruncSatF64U(ExecContext context)
        {
            double value = context.OpStack.PopF64();
            double truncated = Math.Truncate(value);
            uint result = truncated switch
            {
                double.NaN => 0,
                double.PositiveInfinity => uint.MaxValue,
                double.NegativeInfinity => 0,
                < 0 => 0,
                > uint.MaxValue => uint.MaxValue,
                _ => (uint)truncated
            };
            context.OpStack.PushI32((int)result);
        }

        private static void ExecuteI64TruncSatF32S(ExecContext context)
        {
            float value = context.OpStack.PopF32();
            float truncated = (float)Math.Truncate(value);
            long result = truncated switch
            {
                float.NaN => 0,
                float.PositiveInfinity => long.MaxValue,
                float.NegativeInfinity => long.MinValue,
                < long.MinValue => long.MinValue,
                > long.MaxValue => long.MaxValue,
                _ => (long)truncated
            };
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64TruncSatF32U(ExecContext context)
        {
            float value = context.OpStack.PopF32();
            float truncated = (float)Math.Truncate(value);
            ulong result = truncated switch
            {
                float.NaN => 0,
                float.PositiveInfinity => ulong.MaxValue,
                float.NegativeInfinity => 0,
                < 0 => 0,
                > ulong.MaxValue => ulong.MaxValue,
                _ => (ulong)truncated
            };
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64TruncSatF64S(ExecContext context)
        {
            double value = context.OpStack.PopF64();
            double truncated = Math.Truncate(value);
            long result = truncated switch
            {
                double.NaN => 0,
                double.PositiveInfinity => long.MaxValue,
                double.NegativeInfinity => long.MinValue,
                < long.MinValue => long.MinValue,
                > long.MaxValue => long.MaxValue,
                _ => (long)truncated
            };
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64TruncSatF64U(ExecContext context)
        {
            double value = context.OpStack.PopF64();
            double truncated = Math.Truncate(value);
            ulong result = truncated switch
            {
                double.NaN => 0,
                double.PositiveInfinity => ulong.MaxValue,
                double.NegativeInfinity => 0,
                < 0 => 0,
                > ulong.MaxValue => ulong.MaxValue,
                _ => (ulong)truncated
            };
            context.OpStack.PushI64((long)result);
        }
    }
}