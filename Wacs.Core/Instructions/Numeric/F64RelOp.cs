using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F64Eq = new(OpCode.F64Eq, ExecuteF64Eq,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Ne = new(OpCode.F64Ne, ExecuteF64Ne,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Lt = new(OpCode.F64Lt, ExecuteF64Lt,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Gt = new(OpCode.F64Gt, ExecuteF64Gt,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Le = new(OpCode.F64Le, ExecuteF64Le,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Ge = new(OpCode.F64Ge, ExecuteF64Ge,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        private static int CompareF64(double i1, double i2, double epsilon)
        {
            int result = 0;
            result = i1 == i2 ? 1 : 0 ;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            return result;
        }

        private static void ExecuteF64Eq(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();

            int result = i1 == i2 ? 1 : 0;
            
            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Ne(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();

            int result = i1 != i2 ? 1 : 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Lt(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();

            int result = i1 < i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Gt(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();

            int result = i1 > i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Le(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();

            int result = i1 <= i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Ge(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();

            int result = i1 >= i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }
    }
}