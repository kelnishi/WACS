using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly NumericInst I32Add = new(OpCode.I32Add, ExecuteI32Add,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Sub = new(OpCode.I32Sub, ExecuteI32Sub,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Mul = new(OpCode.I32Mul, ExecuteI32Mul,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32DivS = new(OpCode.I32DivS, ExecuteI32DivS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32DivU = new(OpCode.I32DivU, ExecuteI32DivU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32RemS = new(OpCode.I32RemS, ExecuteI32RemS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32RemU = new(OpCode.I32RemU, ExecuteI32RemU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32And = new(OpCode.I32And, ExecuteI32And,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Or = new(OpCode.I32Or, ExecuteI32Or,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Xor = new(OpCode.I32Xor, ExecuteI32Xor,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Shl = new(OpCode.I32Shl, ExecuteI32Shl,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32ShrS = new(OpCode.I32ShrS, ExecuteI32ShrS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32ShrU = new(OpCode.I32ShrU, ExecuteI32ShrU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Rotl = new(OpCode.I32Rotl, ExecuteI32Rotl,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Rotr = new(OpCode.I32Rotr, ExecuteI32Rotr,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        // @Spec 4.3.2.3. iadd
        private static void ExecuteI32Add(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 + i2;
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.4. isub
        private static void ExecuteI32Sub(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 - i2;
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.5. imul
        private static void ExecuteI32Mul(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = unchecked(i1 * i2);
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.7. idiv_s
        private static void ExecuteI32DivS(ExecContext context)
        {
            int j2 = context.OpStack.PopI32();
            int j1 = context.OpStack.PopI32();
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            if (j2 == -1 && j1 == int.MinValue)
                throw new TrapException("Operation results in arithmetic overflow");
            int quotient = j1 / j2;
            context.OpStack.PushI32(quotient);
        }

        // @Spec 4.3.2.6. idiv_u
        private static void ExecuteI32DivU(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            if (i2 == 0)
                throw new TrapException("Cannot divide by zero");
            uint quotient = i1 / i2;
            context.OpStack.PushI32((int)quotient);
        }

        // @Spec 4.3.2.8. irem_s
        private static void ExecuteI32RemS(ExecContext context)
        {
            int j2 = context.OpStack.PopI32();
            int j1 = context.OpStack.PopI32();
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            //Special case for arithmetic overflow
            if (j2 == -1 && j1 == int.MinValue)
            {
                context.OpStack.PushI32(0);
            }
            else
            {
                int remainder = j1 % j2;
                context.OpStack.PushI32(remainder);
            }
        }

        // @Spec 4.3.2.8. irem_u
        private static void ExecuteI32RemU(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            if (i2 == 0)
                throw new TrapException("Cannot divide by zero");
            uint remainder = i1 % i2;
            context.OpStack.PushI32((int)remainder);
        }

        // @Spec 4.3.2.11 iand        
        private static void ExecuteI32And(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            uint result = i1 & i2;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.13 ior
        private static void ExecuteI32Or(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            uint result = i1 | i2;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.14 ixor
        private static void ExecuteI32Xor(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            uint result = i1 ^ i2;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.15 ishl
        private static void ExecuteI32Shl(ExecContext context)
        {
            int k = context.OpStack.PopI32() & 0x1F;
            uint i1 = context.OpStack.PopI32();
            int result = (int)(i1 << k);
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.17 ishr_s
        private static void ExecuteI32ShrS(ExecContext context)
        {
            int k = context.OpStack.PopI32() & 0x1F;
            int i1 = context.OpStack.PopI32();
            int result = i1 >> k;
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.16 ishr_u
        private static void ExecuteI32ShrU(ExecContext context)
        {
            int k = context.OpStack.PopI32() & 0x1F;
            uint i1 = context.OpStack.PopI32();
            uint result = i1 >> k;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.18 irotl
        private static void ExecuteI32Rotl(ExecContext context)
        {
            int k = context.OpStack.PopI32() & 0x1F;
            uint i1 = context.OpStack.PopI32();
            uint result = i1 << k;
            if (k != 0)
                result |= i1 >> (32 - k);

            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.19 irotr
        private static void ExecuteI32Rotr(ExecContext context)
        {
            int k = context.OpStack.PopI32() & 31;
            uint i1 = context.OpStack.PopI32();
            uint result = (i1 >> k) | (i1 << (32 - k));
            context.OpStack.PushI32((int)result);
        }
    }
}