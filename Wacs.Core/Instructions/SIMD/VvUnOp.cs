using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    // VvUnOps - Bit-wise Logical Not
    public partial class NumericInst
    {
        public static readonly NumericInst V128Not = new(SimdCode.V128Not, ExecuteV128Not,
            ValidateOperands(pop: ValType.V128, push: ValType.V128));

        private static void ExecuteV128Not(ExecContext context)
        {
            V128 v1 = context.OpStack.PopV128();
            V128 result = ~v1;
            context.OpStack.PushV128(result);
        }
    }
}