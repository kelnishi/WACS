using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32Eqz = new NumericInst(OpCode.I32Eqz, ExecuteI32Eqz);
        public static readonly NumericInst I32Eq  = new NumericInst(OpCode.I32Eq , ExecuteI32Eq );
        public static readonly NumericInst I32Ne  = new NumericInst(OpCode.I32Ne , ExecuteI32Ne );
        public static readonly NumericInst I32LtS = new NumericInst(OpCode.I32LtS, ExecuteI32LtS);
        public static readonly NumericInst I32LtU = new NumericInst(OpCode.I32LtU, ExecuteI32LtU);
        public static readonly NumericInst I32GtS = new NumericInst(OpCode.I32GtS, ExecuteI32GtS);
        public static readonly NumericInst I32GtU = new NumericInst(OpCode.I32GtU, ExecuteI32GtU);
        public static readonly NumericInst I32LeS = new NumericInst(OpCode.I32LeS, ExecuteI32LeS);
        public static readonly NumericInst I32LeU = new NumericInst(OpCode.I32LeU, ExecuteI32LeU);
        public static readonly NumericInst I32GeS = new NumericInst(OpCode.I32GeS, ExecuteI32GeS);
        public static readonly NumericInst I32GeU = new NumericInst(OpCode.I32GeU, ExecuteI32GeU);
        
        private static void ExecuteI32Eqz(ExecContext context)
        {
            int i = context.Stack.PopI32();
            int result = (i == 0) ? 1 : 0;
            context.Stack.PushI32(result);
        }
        private static void ExecuteI32Eq(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = (a == b) ? 1 : 0;
            context.Stack.PushI32(result);
        }
        private static void ExecuteI32Ne(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = (a != b) ? 1 : 0;
            context.Stack.PushI32(result);
        }
        private static void ExecuteI32LtS(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = ((a < b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32LtU(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            int result = ((a < b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32GtS(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = ((a > b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32GtU(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            int result = ((a > b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32LeS(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = ((a <= b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32LeU(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            int result = ((a <= b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32GeS(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = ((a >= b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32GeU(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            int result = ((a >= b) ? 1 : 0);
            context.Stack.PushI32(result);
        }
    }
}