using System;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F64Abs      = new NumericInst(OpCode.F64Abs       , ExecuteF64Abs      );
        public static readonly NumericInst F64Neg      = new NumericInst(OpCode.F64Neg       , ExecuteF64Neg      );
        public static readonly NumericInst F64Ceil     = new NumericInst(OpCode.F64Ceil      , ExecuteF64Ceil     );
        public static readonly NumericInst F64Floor    = new NumericInst(OpCode.F64Floor     , ExecuteF64Floor    );
        public static readonly NumericInst F64Trunc    = new NumericInst(OpCode.F64Trunc     , ExecuteF64Trunc    );
        public static readonly NumericInst F64Nearest  = new NumericInst(OpCode.F64Nearest   , ExecuteF64Nearest  );
        public static readonly NumericInst F64Sqrt     = new NumericInst(OpCode.F64Sqrt      , ExecuteF64Sqrt     );
        public static readonly NumericInst F64Add      = new NumericInst(OpCode.F64Add       , ExecuteF64Add      );
        public static readonly NumericInst F64Sub      = new NumericInst(OpCode.F64Sub       , ExecuteF64Sub      );
        public static readonly NumericInst F64Mul      = new NumericInst(OpCode.F64Mul       , ExecuteF64Mul      );
        public static readonly NumericInst F64Div      = new NumericInst(OpCode.F64Div       , ExecuteF64Div      );
        public static readonly NumericInst F64Min      = new NumericInst(OpCode.F64Min       , ExecuteF64Min      );
        public static readonly NumericInst F64Max      = new NumericInst(OpCode.F64Max       , ExecuteF64Max      );
        public static readonly NumericInst F64Copysign = new NumericInst(OpCode.F64Copysign  , ExecuteF64Copysign );
        
        private static void ExecuteF64Abs(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = Math.Abs(a);
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Neg(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = -a;
            context.Stack.PushF64(result);
        }
        private static void ExecuteF64Ceil(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Ceiling(a)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Floor(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Floor(a)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Trunc(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = Math.Truncate(a);
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Nearest(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Round(a, MidpointRounding.ToEven)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Sqrt(ExecContext context)
        {
            double a = context.Stack.PopF64();
            var result = Math.Sqrt(a);
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Add(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            var result = a + b;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Sub(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            var result = a - b;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Mul(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            var result = a * b;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Div(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            var result = a / b;
            if (double.IsInfinity(result)) 
                context.Stack.PushF64(double.PositiveInfinity);
            else if (double.IsNaN(result))
                context.Stack.PushF64(double.NaN);
            else
                context.Stack.PushF64(result);
        }

        private static void ExecuteF64Min(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            var result = Math.Min(a, b);
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64Max(ExecContext context)
        {
            double a = context.Stack.PopF64();
            double b = context.Stack.PopF64();
            var result = Math.Max(a, b);
            context.Stack.PushF64(result);
        }
        
        // Mask for the sign bit (most significant bit)
        private const UInt64 F64SignMask = 0x8000_0000_0000_0000;
        private const UInt64 F64NotSignMask = ~F64SignMask;
        
        private static void ExecuteF64Copysign(ExecContext context)
        {
            double x = context.Stack.PopF64();
            double y = context.Stack.PopF64();
            // Extract raw integer bits of x and y
            UInt64 xBits = BitConverter.ToUInt64(BitConverter.GetBytes(x), 0);
            UInt64 yBits = BitConverter.ToUInt64(BitConverter.GetBytes(y), 0);
            
            // Extract the sign bit from y
            UInt64 ySign = yBits & F64SignMask;

            // Extract the magnitude bits from x
            UInt64 xMagnitude = xBits & F64NotSignMask;

            // Combine the sign of y with the magnitude of x
            UInt64 resultBits = xMagnitude | ySign;

            // Convert the result bits back to double
            double result = BitConverter.ToDouble(BitConverter.GetBytes(resultBits), 0);
            context.Stack.PushF64(result);
        }
        
        
    }
}