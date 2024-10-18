using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F64Eq = new(OpCode.F64Eq, ExecuteF64Eq, ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));
        public static readonly NumericInst F64Ne = new(OpCode.F64Ne, ExecuteF64Ne, ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));
        public static readonly NumericInst F64Lt = new(OpCode.F64Lt, ExecuteF64Lt, ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));
        public static readonly NumericInst F64Gt = new(OpCode.F64Gt, ExecuteF64Gt, ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));
        public static readonly NumericInst F64Le = new(OpCode.F64Le, ExecuteF64Le, ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));
        public static readonly NumericInst F64Ge = new(OpCode.F64Ge, ExecuteF64Ge, ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        private static void ExecuteF64Eq(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            
            int result = (a == b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        private static void ExecuteF64Ne(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            
            int result = (a != b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF64Lt(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            
            int result = (a < b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF64Gt(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            
            int result = (a > b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF64Le(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            
            int result = (a <= b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF64Ge(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            
            int result = (a >= b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
    }
}