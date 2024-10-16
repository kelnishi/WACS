using System;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        
        public static readonly NumericInst I64Clz    = new NumericInst(OpCode.I64Clz    , ExecuteI64Clz   );
        public static readonly NumericInst I64Ctz    = new NumericInst(OpCode.I64Ctz    , ExecuteI64Ctz   );
        public static readonly NumericInst I64Popcnt = new NumericInst(OpCode.I64Popcnt , ExecuteI64Popcnt);
        public static readonly NumericInst I64Add    = new NumericInst(OpCode.I64Add    , ExecuteI64Add   );
        public static readonly NumericInst I64Sub    = new NumericInst(OpCode.I64Sub    , ExecuteI64Sub   );
        public static readonly NumericInst I64Mul    = new NumericInst(OpCode.I64Mul    , ExecuteI64Mul   );
        public static readonly NumericInst I64DivS   = new NumericInst(OpCode.I64DivS   , ExecuteI64DivS  );
        public static readonly NumericInst I64DivU   = new NumericInst(OpCode.I64DivU   , ExecuteI64DivU  );
        public static readonly NumericInst I64RemS   = new NumericInst(OpCode.I64RemS   , ExecuteI64RemS  );
        public static readonly NumericInst I64RemU   = new NumericInst(OpCode.I64RemU   , ExecuteI64RemU  );
        public static readonly NumericInst I64And    = new NumericInst(OpCode.I64And    , ExecuteI64And   );
        public static readonly NumericInst I64Or     = new NumericInst(OpCode.I64Or     , ExecuteI64Or    );
        public static readonly NumericInst I64Xor    = new NumericInst(OpCode.I64Xor    , ExecuteI64Xor   );
        public static readonly NumericInst I64Shl    = new NumericInst(OpCode.I64Shl    , ExecuteI64Shl   );
        public static readonly NumericInst I64ShrS   = new NumericInst(OpCode.I64ShrS   , ExecuteI64ShrS  );
        public static readonly NumericInst I64ShrU   = new NumericInst(OpCode.I64ShrU   , ExecuteI64ShrU  );
        public static readonly NumericInst I64Rotl   = new NumericInst(OpCode.I64Rotl   , ExecuteI64Rotl  );
        public static readonly NumericInst I64Rotr   = new NumericInst(OpCode.I64Rotr   , ExecuteI64Rotr  );
        
        private static void ExecuteI64Add(IExecContext context)
        {
            long b = context.OpStack.PopI64();
            long a = context.OpStack.PopI64();
            long result = a + b;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64Clz(IExecContext context)
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

        private static void ExecuteI64Ctz(IExecContext context)
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

        private static void ExecuteI64Popcnt(IExecContext context)
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

        private static void ExecuteI64Sub(IExecContext context)
        {
            long b = context.OpStack.PopI64();
            long a = context.OpStack.PopI64();
            long result = a - b;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64Mul(IExecContext context)
        {
            long a = context.OpStack.PopI64();
            long b = context.OpStack.PopI64();
            long result = unchecked(a * b);
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64DivS(IExecContext context)
{
            long dividend = context.OpStack.PopI64();
            long divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            long quotient = dividend / divisor;
            context.OpStack.PushI64(quotient);
    
        }

        private static void ExecuteI64DivU(IExecContext context)
        {
            ulong dividend = context.OpStack.PopI64();
            ulong divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            ulong quotient = dividend / divisor;
            context.OpStack.PushI64((long)quotient);
        }

        private static void ExecuteI64RemS(IExecContext context)
        {
            long dividend = context.OpStack.PopI64();
            long divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            long remainder = dividend % divisor;
            context.OpStack.PushI64(remainder);
        }

        private static void ExecuteI64RemU(IExecContext context)
        {
            ulong dividend = context.OpStack.PopI64();
            ulong divisor = context.OpStack.PopI64();
            if (divisor == 0) 
                throw new InvalidOperationException("Cannot divide by zero");
            ulong remainder = dividend % divisor;
            context.OpStack.PushI64((long)remainder);
        }

        private static void ExecuteI64And(IExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            ulong result = a & b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Or(IExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            ulong result = a | b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Xor(IExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            ulong b = context.OpStack.PopI64();
            ulong result = a ^ b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Shl(IExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            int b = context.OpStack.PopI64() & 0x3F;
            ulong result = a << b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64ShrS(IExecContext context)
        {
            long a = context.OpStack.PopI64();
            int b = context.OpStack.PopI64() & 0x3F;
            long result = a >> b;
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64ShrU(IExecContext context)
        {
            ulong a = context.OpStack.PopI64();
            int b = context.OpStack.PopI64()& 0x3F;
            ulong result = a >> b;
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Rotl(IExecContext context)
        {
            ulong x = context.OpStack.PopI64();
            int shiftDistance = context.OpStack.PopI64()& 0x3F;

            ulong result = (x << shiftDistance);
            if (shiftDistance != 0)
                result |= (x >> (64 - shiftDistance));
            
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64Rotr(IExecContext context)
        {
            ulong x = context.OpStack.PopI64();
            int shiftDistance = context.OpStack.PopI64() & 0x3F;
            ulong result = (x >> shiftDistance) | (x << (64 - shiftDistance));
            context.OpStack.PushI64((long)result);
        }
        
        
    }
}