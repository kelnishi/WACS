using System;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly NumericInst I64Add    = new NumericInst(OpCode.I64Add    , ExecuteI64Add  , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Sub    = new NumericInst(OpCode.I64Sub    , ExecuteI64Sub  , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Mul    = new NumericInst(OpCode.I64Mul    , ExecuteI64Mul  , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64DivS   = new NumericInst(OpCode.I64DivS   , ExecuteI64DivS , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64DivU   = new NumericInst(OpCode.I64DivU   , ExecuteI64DivU , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64RemS   = new NumericInst(OpCode.I64RemS   , ExecuteI64RemS , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64RemU   = new NumericInst(OpCode.I64RemU   , ExecuteI64RemU , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64And    = new NumericInst(OpCode.I64And    , ExecuteI64And  , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Or     = new NumericInst(OpCode.I64Or     , ExecuteI64Or   , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Xor    = new NumericInst(OpCode.I64Xor    , ExecuteI64Xor  , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Shl    = new NumericInst(OpCode.I64Shl    , ExecuteI64Shl  , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64ShrS   = new NumericInst(OpCode.I64ShrS   , ExecuteI64ShrS , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64ShrU   = new NumericInst(OpCode.I64ShrU   , ExecuteI64ShrU , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Rotl   = new NumericInst(OpCode.I64Rotl   , ExecuteI64Rotl , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        public static readonly NumericInst I64Rotr   = new NumericInst(OpCode.I64Rotr   , ExecuteI64Rotr , ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));
        
        private static void ExecuteI64Add(ExecContext context)
        {
            long b = context.OpStack.PopI64();
            long a = context.OpStack.PopI64();
            long result = a + b;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64Sub(ExecContext context)
        {
            long b = context.OpStack.PopI64();
            long a = context.OpStack.PopI64();
            long result = a - b;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64Mul(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            long result = unchecked(a * b);
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64DivS(ExecContext context)
{
            long dividend = context.OpStack.PopI64();
            long divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            long quotient = dividend / divisor;
            context.OpStack.PushI64(quotient);
    
        }

        private static void ExecuteI64DivU(ExecContext context)
        {
            ulong dividend = context.OpStack.PopI64();
            ulong divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            ulong quotient = dividend / divisor;
            context.OpStack.PushI64((long)quotient);
        }

        private static void ExecuteI64RemS(ExecContext context)
        {
            long dividend = context.OpStack.PopI64();
            long divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            long remainder = dividend % divisor;
            context.OpStack.PushI64(remainder);
        }

        private static void ExecuteI64RemU(ExecContext context)
        {
            ulong dividend = context.OpStack.PopI64();
            ulong divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            ulong remainder = dividend % divisor;
            context.OpStack.PushI64((long)remainder);
        }

        private static void ExecuteI64And(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            ulong result = a & b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Or(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            ulong result = a | b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Xor(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            ulong result = a ^ b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Shl(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            int b = context.OpStack.PopI64() & 0x3F;
            ulong result = a << b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64ShrS(ExecContext context)
        {
            long a = context.OpStack.PopI64();
            int b = context.OpStack.PopI64() & 0x3F;
            long result = a >> b;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64ShrU(ExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            int b = context.OpStack.PopI64()& 0x3F;
            ulong result = a >> b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Rotl(ExecContext context)
        {
            ulong x = context.OpStack.PopI64();
            int shiftDistance = context.OpStack.PopI64()& 0x3F;

            ulong result = (x << shiftDistance);
            if (shiftDistance != 0)
                result |= (x >> (64 - shiftDistance));
            
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Rotr(ExecContext context)
        {
            ulong x = context.OpStack.PopI64();
            int shiftDistance = context.OpStack.PopI64() & 0x3F;
            ulong result = (x >> shiftDistance) | (x << (64 - shiftDistance));
            context.OpStack.PushI64((long)result);
        }
        
        
    }
}