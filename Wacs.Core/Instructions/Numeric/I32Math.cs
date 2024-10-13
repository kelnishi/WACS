using System;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32Clz    = new NumericInst(OpCode.I32Clz    , ExecuteI32Clz   );
        public static readonly NumericInst I32Ctz    = new NumericInst(OpCode.I32Ctz    , ExecuteI32Ctz   );
        public static readonly NumericInst I32Popcnt = new NumericInst(OpCode.I32Popcnt , ExecuteI32Popcnt);
        public static readonly NumericInst I32Add    = new NumericInst(OpCode.I32Add    , ExecuteI32Add   );
        public static readonly NumericInst I32Sub    = new NumericInst(OpCode.I32Sub    , ExecuteI32Sub   );
        public static readonly NumericInst I32Mul    = new NumericInst(OpCode.I32Mul    , ExecuteI32Mul   );
        public static readonly NumericInst I32DivS   = new NumericInst(OpCode.I32DivS   , ExecuteI32DivS  );
        public static readonly NumericInst I32DivU   = new NumericInst(OpCode.I32DivU   , ExecuteI32DivU  );
        public static readonly NumericInst I32RemS   = new NumericInst(OpCode.I32RemS   , ExecuteI32RemS  );
        public static readonly NumericInst I32RemU   = new NumericInst(OpCode.I32RemU   , ExecuteI32RemU  );
        public static readonly NumericInst I32And    = new NumericInst(OpCode.I32And    , ExecuteI32And   );
        public static readonly NumericInst I32Or     = new NumericInst(OpCode.I32Or     , ExecuteI32Or    );
        public static readonly NumericInst I32Xor    = new NumericInst(OpCode.I32Xor    , ExecuteI32Xor   );
        public static readonly NumericInst I32Shl    = new NumericInst(OpCode.I32Shl    , ExecuteI32Shl   );
        public static readonly NumericInst I32ShrS   = new NumericInst(OpCode.I32ShrS   , ExecuteI32ShrS  );
        public static readonly NumericInst I32ShrU   = new NumericInst(OpCode.I32ShrU   , ExecuteI32ShrU  );
        public static readonly NumericInst I32Rotl   = new NumericInst(OpCode.I32Rotl   , ExecuteI32Rotl  );
        public static readonly NumericInst I32Rotr   = new NumericInst(OpCode.I32Rotr   , ExecuteI32Rotr  );
        
        // @Spec 4.3.2.3. iadd
        private static void ExecuteI32Add(ExecContext context)
        {
            int b = context.Stack.PopI32();
            int a = context.Stack.PopI32();
            int result = a + b;
            context.Stack.PushI32(result);
        }
        
        // @Spec 4.3.2.20 iclz
        private static void ExecuteI32Clz(ExecContext context)
        {
            int value = context.Stack.PopI32();
            uint clz = 0;
            while (value != 0) 
            {
                clz++;
                value >>= 1;
            }
            context.Stack.PushI32((int)clz);
        }
        
        // @Spec 4.3.2.21 ictz
        private static void ExecuteI32Ctz(ExecContext context)
        {
            int value = context.Stack.PopI32();
            uint ctz = 0;
            while ((value & 1) == 0) 
            {
                ctz++;
                value >>= 1;
            }
            context.Stack.PushI32((int)ctz);
        }

        // @Spec 4.3.2.22 ipopcnt
        private static void ExecuteI32Popcnt(ExecContext context)
        {
            int x = context.Stack.PopI32();
            uint popcnt = 0;
            while ((x & 1) != 0)
            {
                popcnt++;
                x >>= 1;
            }
            context.Stack.PushI32((int)popcnt);
        }

        // @Spec 4.3.2.4. isub
        private static void ExecuteI32Sub(ExecContext context)
        {
            int b = context.Stack.PopI32();
            int a = context.Stack.PopI32();
            int result = a - b;
            context.Stack.PushI32(result);
        }

        // @Spec 4.3.2.5. imul
        private static void ExecuteI32Mul(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32();
            int result = unchecked(a * b);
            context.Stack.PushI64(result);
        }

        // @Spec 4.3.2.7. idiv_s
        private static void ExecuteI32DivS(ExecContext context)
{
            int dividend = context.Stack.PopI32();
            int divisor = context.Stack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            int quotient = dividend / divisor;
            context.Stack.PushI32(quotient);
    
        }

        // @Spec 4.3.2.6. idiv_u
        private static void ExecuteI32DivU(ExecContext context)
        {
            uint dividend = context.Stack.PopI32();
            uint divisor = context.Stack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            uint quotient = dividend / divisor;
            context.Stack.PushI32((int)quotient);
        }

        // @Spec 4.3.2.8. irem_s
        private static void ExecuteI32RemS(ExecContext context)
        {
            int dividend = context.Stack.PopI32();
            int divisor = context.Stack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            int remainder = dividend % divisor;
            context.Stack.PushI32(remainder);
        }

        // @Spec 4.3.2.8. irem_u
        private static void ExecuteI32RemU(ExecContext context)
        {
            uint dividend = context.Stack.PopI32();
            uint divisor = context.Stack.PopI32();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            uint remainder = dividend % divisor;
            context.Stack.PushI32((int)remainder);
        }

        // @Spec 4.3.2.11 iand        
        private static void ExecuteI32And(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            uint result = a & b;
            context.Stack.PushI32((int)result);
        }

        // @Spec 4.3.2.13 ior
        private static void ExecuteI32Or(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            uint result = a | b;
            context.Stack.PushI32((int)result);
        }

        // @Spec 4.3.2.14 ixor
        private static void ExecuteI32Xor(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            uint b = context.Stack.PopI32();
            uint result = a ^ b;
            context.Stack.PushI32((int)result);
        }

        // @Spec 4.3.2.15 ishl
        private static void ExecuteI32Shl(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            int b = context.Stack.PopI32() & 0x1F;
            int result = (int)(a << b);
            context.Stack.PushI32(result);
        }

        // @Spec 4.3.2.17 ishr_s
        private static void ExecuteI32ShrS(ExecContext context)
        {
            int a = context.Stack.PopI32();
            int b = context.Stack.PopI32() & 0x1F;
            int result = a >> b;
            context.Stack.PushI32(result);
        }

        // @Spec 4.3.2.16 ishr_u
        private static void ExecuteI32ShrU(ExecContext context)
        {
            uint a = context.Stack.PopI32();
            int b = context.Stack.PopI32()& 0x1F;
            uint result = a >> b;
            context.Stack.PushI32((int)result);
        }

        // @Spec 4.3.2.18 irotl
        private static void ExecuteI32Rotl(ExecContext context)
        {
            uint x = context.Stack.PopI32();
            int shiftDistance = context.Stack.PopI32()& 0x1F;

            uint result = (x << shiftDistance);
            if (shiftDistance != 0)
                result |= (x >> (32 - shiftDistance));
            
            context.Stack.PushI32((int)result);
        }

        // @Spec 4.3.2.19 irotr
        private static void ExecuteI32Rotr(ExecContext context)
        {
            uint x = context.Stack.PopI32();
            int shiftDistance = context.Stack.PopI32() & 31;
            uint result = (x >> shiftDistance) | (x << (32 - shiftDistance));
            context.Stack.PushI32((int)result);
        }
    }
}