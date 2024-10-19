using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32Eq = new(OpCode.F32Eq, ExecuteF32Eq,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Ne = new(OpCode.F32Ne, ExecuteF32Ne,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Lt = new(OpCode.F32Lt, ExecuteF32Lt,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Gt = new(OpCode.F32Gt, ExecuteF32Gt,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Le = new(OpCode.F32Le, ExecuteF32Le,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Ge = new(OpCode.F32Ge, ExecuteF32Ge,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        private static void ExecuteF32Eq(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();

            int result = Math.Abs(a - b) < context.Attributes.FloatingPointTolerance ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Ne(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();

            int result = Math.Abs(a - b) > context.Attributes.FloatingPointTolerance ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Lt(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();

            int result = a < b ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Gt(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();

            int result = a > b ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Le(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();

            int result = a <= b ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Ge(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();

            int result = a >= b ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;

            context.OpStack.PushI32(result);
        }
    }
}