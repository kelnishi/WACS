using System;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly NumericInst I32Add    = new(OpCode.I32Add    , ExecuteI32Add  , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Sub    = new(OpCode.I32Sub    , ExecuteI32Sub  , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Mul    = new(OpCode.I32Mul    , ExecuteI32Mul  , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32DivS   = new(OpCode.I32DivS   , ExecuteI32DivS , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32DivU   = new(OpCode.I32DivU   , ExecuteI32DivU , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32RemS   = new(OpCode.I32RemS   , ExecuteI32RemS , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32RemU   = new(OpCode.I32RemU   , ExecuteI32RemU , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32And    = new(OpCode.I32And    , ExecuteI32And  , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Or     = new(OpCode.I32Or     , ExecuteI32Or   , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Xor    = new(OpCode.I32Xor    , ExecuteI32Xor  , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Shl    = new(OpCode.I32Shl    , ExecuteI32Shl  , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32ShrS   = new(OpCode.I32ShrS   , ExecuteI32ShrS , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32ShrU   = new(OpCode.I32ShrU   , ExecuteI32ShrU , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Rotl   = new(OpCode.I32Rotl   , ExecuteI32Rotl , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32Rotr   = new(OpCode.I32Rotr   , ExecuteI32Rotr , ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));
        
        // @Spec 4.3.2.3. iadd
        private static void ExecuteI32Add(ExecContext context)
        {
            int b = context.OpStack.PopI32();
            int a = context.OpStack.PopI32();
            int result = a + b;
            context.OpStack.PushI32(result);
        }
        
        // @Spec 4.3.2.4. isub
        private static void ExecuteI32Sub(ExecContext context)
        {
            int b = context.OpStack.PopI32();
            int a = context.OpStack.PopI32();
            int result = a - b;
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.5. imul
        private static void ExecuteI32Mul(ExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32();
            int result = unchecked(a * b);
            context.OpStack.PushI64(result);
        }

        // @Spec 4.3.2.7. idiv_s
        private static void ExecuteI32DivS(ExecContext context)
{
            int dividend = context.OpStack.PopI32();
            int divisor = context.OpStack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            int quotient = dividend / divisor;
            context.OpStack.PushI32(quotient);
    
        }

        // @Spec 4.3.2.6. idiv_u
        private static void ExecuteI32DivU(ExecContext context)
        {
            uint dividend = context.OpStack.PopI32();
            uint divisor = context.OpStack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            uint quotient = dividend / divisor;
            context.OpStack.PushI32((int)quotient);
        }

        // @Spec 4.3.2.8. irem_s
        private static void ExecuteI32RemS(ExecContext context)
        {
            int dividend = context.OpStack.PopI32();
            int divisor = context.OpStack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            int remainder = dividend % divisor;
            context.OpStack.PushI32(remainder);
        }

        // @Spec 4.3.2.8. irem_u
        private static void ExecuteI32RemU(ExecContext context)
        {
            uint dividend = context.OpStack.PopI32();
            uint divisor = context.OpStack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            uint remainder = dividend % divisor;
            context.OpStack.PushI32((int)remainder);
        }

        // @Spec 4.3.2.11 iand        
        private static void ExecuteI32And(ExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            uint result = a & b;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.13 ior
        private static void ExecuteI32Or(ExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            uint result = a | b;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.14 ixor
        private static void ExecuteI32Xor(ExecContext context)
        {
            uint a = context.OpStack.PopI32();
            uint b = context.OpStack.PopI32();
            uint result = a ^ b;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.15 ishl
        private static void ExecuteI32Shl(ExecContext context)
        {
            uint a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32() & 0x1F;
            int result = (int)(a << b);
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.17 ishr_s
        private static void ExecuteI32ShrS(ExecContext context)
        {
            int a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32() & 0x1F;
            int result = a >> b;
            context.OpStack.PushI32(result);
        }

        // @Spec 4.3.2.16 ishr_u
        private static void ExecuteI32ShrU(ExecContext context)
        {
            uint a = context.OpStack.PopI32();
            int b = context.OpStack.PopI32()& 0x1F;
            uint result = a >> b;
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.18 irotl
        private static void ExecuteI32Rotl(ExecContext context)
        {
            uint x = context.OpStack.PopI32();
            int shiftDistance = context.OpStack.PopI32()& 0x1F;

            uint result = (x << shiftDistance);
            if (shiftDistance != 0)
                result |= (x >> (32 - shiftDistance));
            
            context.OpStack.PushI32((int)result);
        }

        // @Spec 4.3.2.19 irotr
        private static void ExecuteI32Rotr(ExecContext context)
        {
            uint x = context.OpStack.PopI32();
            int shiftDistance = context.OpStack.PopI32() & 31;
            uint result = (x >> shiftDistance) | (x << (32 - shiftDistance));
            context.OpStack.PushI32((int)result);
        }
    }
}