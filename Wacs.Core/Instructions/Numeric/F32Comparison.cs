using System;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32Eq = new NumericInst(OpCode.F32Eq, ExecuteF32Eq);
        public static readonly NumericInst F32Ne = new NumericInst(OpCode.F32Ne, ExecuteF32Ne);
        public static readonly NumericInst F32Lt = new NumericInst(OpCode.F32Lt, ExecuteF32Lt);
        public static readonly NumericInst F32Gt = new NumericInst(OpCode.F32Gt, ExecuteF32Gt);
        public static readonly NumericInst F32Le = new NumericInst(OpCode.F32Le, ExecuteF32Le);
        public static readonly NumericInst F32Ge = new NumericInst(OpCode.F32Ge, ExecuteF32Ge);
        
        private static void ExecuteF32Eq(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            
            int result = (a == b) ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        private static void ExecuteF32Ne(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            
            int result = (a != b) ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF32Lt(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            
            int result = (a < b) ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF32Gt(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            
            int result = (a > b) ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF32Le(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            
            int result = (a <= b) ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteF32Ge(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            
            int result = (a >= b) ? 1 : 0;
            if (float.IsNaN(a) || float.IsNaN(b))
                result = 0;
            
            context.OpStack.PushI32(result);
        }
        
    }
}