using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly NumericInst I64Add = new(OpCode.I64Add, ExecuteI64Add,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Sub = new(OpCode.I64Sub, ExecuteI64Sub,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Mul = new(OpCode.I64Mul, ExecuteI64Mul,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64DivS = new(OpCode.I64DivS, ExecuteI64DivS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64DivU = new(OpCode.I64DivU, ExecuteI64DivU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64RemS = new(OpCode.I64RemS, ExecuteI64RemS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64RemU = new(OpCode.I64RemU, ExecuteI64RemU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64And = new(OpCode.I64And, ExecuteI64And,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Or = new(OpCode.I64Or, ExecuteI64Or,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Xor = new(OpCode.I64Xor, ExecuteI64Xor,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Shl = new(OpCode.I64Shl, ExecuteI64Shl,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64ShrS = new(OpCode.I64ShrS, ExecuteI64ShrS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64ShrU = new(OpCode.I64ShrU, ExecuteI64ShrU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Rotl = new(OpCode.I64Rotl, ExecuteI64Rotl,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly NumericInst I64Rotr = new(OpCode.I64Rotr, ExecuteI64Rotr,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        private static void ExecuteI64Add(ExecContext context)
        {
            long i2 = context.OpStack.PopI64();
            long i1 = context.OpStack.PopI64();
            long result = i1 + i2;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64Sub(ExecContext context)
        {
            long i2 = context.OpStack.PopI64();
            long i1 = context.OpStack.PopI64();
            long result = i1 - i2;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64Mul(ExecContext context)
        {
            long i2 = context.OpStack.PopI64();
            long i1 = context.OpStack.PopI64();
            long result = unchecked(i1 * i2);
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64DivS(ExecContext context)
        {
            long j2 = context.OpStack.PopI64();
            long j1 = context.OpStack.PopI64();
            if (j2 == 0)
                throw new InvalidOperationException("Cannot divide by zero");
            long quotient = j1 / j2;
            context.OpStack.PushI64(quotient);
        }

        private static void ExecuteI64DivU(ExecContext context)
        {
            ulong j2 = context.OpStack.PopI64();
            ulong j1 = context.OpStack.PopI64();
            if (j2 == 0)
                throw new InvalidOperationException("Cannot divide by zero");
            ulong quotient = j1 / j2;
            context.OpStack.PushI64((long)quotient);
        }

        private static void ExecuteI64RemS(ExecContext context)
        {
            long j2 = context.OpStack.PopI64();
            long j1 = context.OpStack.PopI64();
            if (j2 == 0)
                throw new InvalidOperationException("Cannot divide by zero");
            long remainder = j1 % j2;
            context.OpStack.PushI64(remainder);
        }

        private static void ExecuteI64RemU(ExecContext context)
        {
            ulong j2 = context.OpStack.PopI64();
            ulong j1 = context.OpStack.PopI64();
            if (j2 == 0)
                throw new InvalidOperationException("Cannot divide by zero");
            ulong remainder = j1 % j2;
            context.OpStack.PushI64((long)remainder);
        }

        private static void ExecuteI64And(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64();
            ulong i1 = context.OpStack.PopI64();
            ulong result = i1 & i2;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Or(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64();
            ulong i1 = context.OpStack.PopI64();
            ulong result = i1 | i2;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Xor(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64();
            ulong i1 = context.OpStack.PopI64();
            ulong result = i1 ^ i2;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Shl(ExecContext context)
        {
            int k = context.OpStack.PopI64() & 0x3F;
            ulong i1 = context.OpStack.PopI64();
            ulong result = i1 << k;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64ShrS(ExecContext context)
        {
            int k = context.OpStack.PopI64() & 0x3F;
            long i1 = context.OpStack.PopI64();
            long result = i1 >> k;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64ShrU(ExecContext context)
        {
            int k = context.OpStack.PopI64() & 0x3F;
            ulong i1 = context.OpStack.PopI64();
            ulong result = i1 >> k;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Rotl(ExecContext context)
        {
            int k = context.OpStack.PopI64() & 0x3F;
            ulong i1 = context.OpStack.PopI64();

            ulong result = i1 << k;
            if (k != 0)
                result |= i1 >> (64 - k);

            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Rotr(ExecContext context)
        {
            int k = context.OpStack.PopI64() & 0x3F;
            ulong i1 = context.OpStack.PopI64();
            ulong result = (i1 >> k) | (i1 << (64 - k));
            context.OpStack.PushI64((long)result);
        }
    }
}