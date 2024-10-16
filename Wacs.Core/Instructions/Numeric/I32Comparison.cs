using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

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
        
        private static void ExecuteI32Eqz(IExecContext context)
        {
            int i = context.OpStack.PopI32();
            int result = (i == 0) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
        private static void ExecuteI32Eq(IExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = (a == b) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
        private static void ExecuteI32Ne(IExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = (a != b) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
        private static void ExecuteI32LtS(IExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = ((a < b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32LtU(IExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            int result = ((a < b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32GtS(IExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = ((a > b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32GtU(IExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            int result = ((a > b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32LeS(IExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = ((a <= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32LeU(IExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            int result = ((a <= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32GeS(IExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = ((a >= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
        
        private static void ExecuteI32GeU(IExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            int result = ((a >= b) ? 1 : 0);
            context.OpStack.PushI32(result);
        }
    }
}