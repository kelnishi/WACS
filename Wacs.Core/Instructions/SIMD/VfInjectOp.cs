using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    // VvBinOps - Bit-wise Logical Operators
    public partial class NumericInst
    {
        public static readonly NumericInst F32x4Splat = new(SimdCode.F32x4Splat, ExecuteF32x4Splat, ValidateOperands(pop: ValType.F32, push: ValType.V128));
        public static readonly NumericInst F64x2Splat = new(SimdCode.F64x2Splat, ExecuteF64x2Splat, ValidateOperands(pop: ValType.F64, push: ValType.V128));

        private static void ExecuteF32x4Splat(ExecContext context)
        {
            float v = context.OpStack.PopF32();
            V128 result = new V128(v, v, v, v);
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Splat(ExecContext context)
        {
            double v = context.OpStack.PopF64();
            V128 result = new V128(v, v);
            context.OpStack.PushV128(result);
        }
    }
}