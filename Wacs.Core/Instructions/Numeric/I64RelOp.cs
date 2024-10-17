using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.5. i.relop
        public static readonly NumericInst I64Eq  = new(OpCode.I64Eq,  ExecuteI64Eq , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64Ne  = new(OpCode.I64Ne,  ExecuteI64Ne , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32)); 
        public static readonly NumericInst I64LtS = new(OpCode.I64LtS, ExecuteI64LtS, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64LtU = new(OpCode.I64LtU, ExecuteI64LtU, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64GtS = new(OpCode.I64GtS, ExecuteI64GtS, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64GtU = new(OpCode.I64GtU, ExecuteI64GtU, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64LeS = new(OpCode.I64LeS, ExecuteI64LeS, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64LeU = new(OpCode.I64LeU, ExecuteI64LeU, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64GeS = new(OpCode.I64GeS, ExecuteI64GeS, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));
        public static readonly NumericInst I64GeU = new(OpCode.I64GeU, ExecuteI64GeU, ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

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