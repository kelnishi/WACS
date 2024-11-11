using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // New entry for the V128BitSelect operation
        public static readonly NumericInst V128BitSelect = new(SimdCode.V128BitSelect, ExecuteV128BitSelect,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128));

        /// <summary>
        /// @Spec 4.3.2.35. ibitselect
        /// </summary>
        /// <param name="context"></param>
        private static void ExecuteV128BitSelect(ExecContext context)
        {
            V128 i3 = context.OpStack.PopV128(); // Bits to select from if not set
            V128 i2 = context.OpStack.PopV128(); // Bits to select from if set
            V128 i1 = context.OpStack.PopV128(); // Selection mask

            V128 j1 = i1 & i3;
            V128 j3 = ~i3;
            V128 j2 = i2 & j3;
            V128 result = j1 | j2;
            
            context.OpStack.PushV128(result);
        }
    }
}