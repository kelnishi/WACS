using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.4 i.testop
        public static readonly NumericInst I32Eqz = new(OpCode.I32Eqz, ExecuteI32Eqz, ValidateOperands(push: ValType.I32, pop: ValType.I32));
        public static readonly NumericInst I64Eqz = new(OpCode.I64Eqz, ExecuteI64Eqz, ValidateOperands(push: ValType.I64, pop: ValType.I32));

        private static void ExecuteI32Eqz(ExecContext context)
        {
            int i = context.OpStack.PopI32();
            int result = (i == 0) ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64Eqz(ExecContext context)
        {
            long i = context.OpStack.PopI64();
            int result = (i == 0) ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}