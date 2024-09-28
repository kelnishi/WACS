using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F64Eq = new NumericInst(OpCode.F64Eq, ExecuteF64Eq);
        public static readonly NumericInst F64Ne = new NumericInst(OpCode.F64Ne, ExecuteF64Ne);
        public static readonly NumericInst F64Lt = new NumericInst(OpCode.F64Lt, ExecuteF64Lt);
        public static readonly NumericInst F64Gt = new NumericInst(OpCode.F64Gt, ExecuteF64Gt);
        public static readonly NumericInst F64Le = new NumericInst(OpCode.F64Le, ExecuteF64Le);
        public static readonly NumericInst F64Ge = new NumericInst(OpCode.F64Ge, ExecuteF64Ge);

        private static void ExecuteF64Eq(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            
            int result = (a == b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.Stack.PushI32(result);
        }
        private static void ExecuteF64Ne(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            
            int result = (a != b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteF64Lt(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            
            int result = (a < b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteF64Gt(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            
            int result = (a > b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteF64Le(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            
            int result = (a <= b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteF64Ge(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            
            int result = (a >= b) ? 1 : 0;
            if (double.IsNaN(a) || double.IsNaN(b))
                result = 0;
            
            context.Stack.PushI32(result);
        }
    }
}