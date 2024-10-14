using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I64Eqz = new NumericInst(OpCode.I64Eqz, ExecuteI64Eqz);
        public static readonly NumericInst I64Eq  = new NumericInst(OpCode.I64Eq,  ExecuteI64Eq );
        public static readonly NumericInst I64Ne  = new NumericInst(OpCode.I64Ne,  ExecuteI64Ne ); 
        public static readonly NumericInst I64LtS = new NumericInst(OpCode.I64LtS, ExecuteI64LtS);
        public static readonly NumericInst I64LtU = new NumericInst(OpCode.I64LtU, ExecuteI64LtU);
        public static readonly NumericInst I64GtS = new NumericInst(OpCode.I64GtS, ExecuteI64GtS);
        public static readonly NumericInst I64GtU = new NumericInst(OpCode.I64GtU, ExecuteI64GtU);
        public static readonly NumericInst I64LeS = new NumericInst(OpCode.I64LeS, ExecuteI64LeS);
        public static readonly NumericInst I64LeU = new NumericInst(OpCode.I64LeU, ExecuteI64LeU);
        public static readonly NumericInst I64GeS = new NumericInst(OpCode.I64GeS, ExecuteI64GeS);
        public static readonly NumericInst I64GeU = new NumericInst(OpCode.I64GeU, ExecuteI64GeU);
        
       private static void ExecuteI64Eqz(ExecContext context)
        {
            long i = context.OpStack.PopI64();
            int result = (i == 0) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
        private static void ExecuteI64Eq(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            int result = (a == b) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
        private static void ExecuteI64Ne(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            int result = (a != b) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
        private static void ExecuteI64LtS(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            int result = ((a < b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64LtU(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            int result = ((a < b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64GtS(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            int result = ((a > b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64GtU(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            int result = ((a > b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64LeS(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            int result = ((a <= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64LeU(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            int result = ((a <= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64GeS(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            int result = ((a >= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI64GeU(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            int result = ((a >= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
    }
}