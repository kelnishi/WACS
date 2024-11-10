using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    // VvBinOps - Bit-wise Logical Operators
    public partial class NumericInst
    {
        public static readonly NumericInst V128And = new(SimdCode.V128And, ExecuteV128And,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst V128AndNot = new(SimdCode.V128AndNot, ExecuteV128AndNot,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst V128Or = new(SimdCode.V128Or, ExecuteV128Or,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst V128Xor = new(SimdCode.V128Xor, ExecuteV128Xor,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        private static void ExecuteV128And(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = v1 & v2;
            context.OpStack.PushV128(result);
        }

        private static void ExecuteV128AndNot(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = v1 & ~v2;
            context.OpStack.PushV128(result);
        }

        private static void ExecuteV128Or(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = v1 | v2;
            context.OpStack.PushV128(result);
        }

        private static void ExecuteV128Xor(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = v1 ^ v2;
            context.OpStack.PushV128(result);
        }
    }
}