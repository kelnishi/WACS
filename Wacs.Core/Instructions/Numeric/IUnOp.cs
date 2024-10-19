using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.2. i.unop
        public static readonly NumericInst I32Clz    = new(OpCode.I32Clz    , ExecuteI32Clz    , ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Ctz    = new(OpCode.I32Ctz    , ExecuteI32Ctz    , ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Popcnt = new(OpCode.I32Popcnt , ExecuteI32Popcnt , ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I64Clz    = new(OpCode.I64Clz    , ExecuteI64Clz    , ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Ctz    = new(OpCode.I64Ctz    , ExecuteI64Ctz    , ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Popcnt = new(OpCode.I64Popcnt , ExecuteI64Popcnt , ValidateOperands(pop: ValType.I64, push: ValType.I64));

        // @Spec 4.3.2.20 iclz
        private static void ExecuteI32Clz(ExecContext context)
        {
            int value = context.OpStack.PopI32();
            uint clz = 0;
            while (value != 0) 
            {
                clz++;
                value >>= 1;
            }
            context.OpStack.PushI32((int)clz);
        }

        // @Spec 4.3.2.21 ictz
        private static void ExecuteI32Ctz(ExecContext context)
        {
            int value = context.OpStack.PopI32();
            uint ctz = 0;
            while ((value & 1) == 0) 
            {
                ctz++;
                value >>= 1;
            }
            context.OpStack.PushI32((int)ctz);
        }

        // @Spec 4.3.2.22 ipopcnt
        private static void ExecuteI32Popcnt(ExecContext context)
        {
            int x = context.OpStack.PopI32();
            uint popcnt = 0;
            while ((x & 1) != 0)
            {
                popcnt++;
                x >>= 1;
            }
            context.OpStack.PushI32((int)popcnt);
        }

        private static void ExecuteI64Clz(ExecContext context)
        {
            long value = context.OpStack.PopI64();
            long clz = 0;
            while (value != 0) 
            {
                clz++;
                value >>= 1;
            }
            context.OpStack.PushI64(clz);
        }

        private static void ExecuteI64Ctz(ExecContext context)
        {
            long value = context.OpStack.PopI64();
            long ctz = 0;
            while ((value & 1) == 0) 
            {
                ctz++;
                value >>= 1;
            }
            context.OpStack.PushI64(ctz);
        }

        private static void ExecuteI64Popcnt(ExecContext context)
        {
            long x = context.OpStack.PopI64();
            long popcnt = 0;
            while ((x & 1) != 0)
            {
                popcnt++;
                x >>= 1;
            }
            context.OpStack.PushI64(popcnt);
        }
    }
}