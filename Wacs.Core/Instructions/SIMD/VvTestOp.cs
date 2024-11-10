using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst V128AnyTrue = new(SimdCode.V128AnyTrue, ExecuteV128AnyTrue,
            ValidateOperands(pop: ValType.V128, push: ValType.I32));

        private static void ExecuteV128AnyTrue(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int result = (c.U64x2_0 & 0xFFFF_FFFF_FFFF_FFFF) != 0UL || (c.U64x2_1 & 0xFFFF_FFFF_FFFF_FFFF) != 0UL ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}