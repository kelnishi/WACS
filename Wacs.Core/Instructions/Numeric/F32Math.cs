using System;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        
        public static readonly NumericInst F32Abs      = new NumericInst(OpCode.F32Abs       , ExecuteF32Abs      );
        public static readonly NumericInst F32Neg      = new NumericInst(OpCode.F32Neg       , ExecuteF32Neg      );
        public static readonly NumericInst F32Ceil     = new NumericInst(OpCode.F32Ceil      , ExecuteF32Ceil     );
        public static readonly NumericInst F32Floor    = new NumericInst(OpCode.F32Floor     , ExecuteF32Floor    );
        public static readonly NumericInst F32Trunc    = new NumericInst(OpCode.F32Trunc     , ExecuteF32Trunc    );
        public static readonly NumericInst F32Nearest  = new NumericInst(OpCode.F32Nearest   , ExecuteF32Nearest  );
        public static readonly NumericInst F32Sqrt     = new NumericInst(OpCode.F32Sqrt      , ExecuteF32Sqrt     );
        public static readonly NumericInst F32Add      = new NumericInst(OpCode.F32Add       , ExecuteF32Add      );
        public static readonly NumericInst F32Sub      = new NumericInst(OpCode.F32Sub       , ExecuteF32Sub      );
        public static readonly NumericInst F32Mul      = new NumericInst(OpCode.F32Mul       , ExecuteF32Mul      );
        public static readonly NumericInst F32Div      = new NumericInst(OpCode.F32Div       , ExecuteF32Div      );
        public static readonly NumericInst F32Min      = new NumericInst(OpCode.F32Min       , ExecuteF32Min      );
        public static readonly NumericInst F32Max      = new NumericInst(OpCode.F32Max       , ExecuteF32Max      );
        public static readonly NumericInst F32Copysign = new NumericInst(OpCode.F32Copysign  , ExecuteF32Copysign );
        
        private static void ExecuteF32Abs(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            var result = Math.Abs(a);
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Neg(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            var result = -a;
            context.OpStack.PushF32(result);
        }
        private static void ExecuteF32Ceil(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            var result = a switch {
                _ when float.IsNaN(a) => float.NaN,
                _ when float.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0f => a,
                _ => (float)Math.Ceiling(a)
            };
            if (result == 0.0f && a < 0.0f)
                result = -0.0f;
            
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Floor(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            var result = a switch {
                _ when float.IsNaN(a) => float.NaN,
                _ when float.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0f => a,
                _ => (float)Math.Floor(a)
            };
            if (result == 0.0f && a < 0.0f)
                result = -0.0f;
            
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Trunc(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float result = (float)Math.Truncate(a);
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Nearest(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            var result = a switch {
                _ when float.IsNaN(a) => float.NaN,
                _ when float.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0f => a,
                _ => (float)Math.Round(a, MidpointRounding.ToEven)
            };
            if (result == 0.0f && a < 0.0f)
                result = -0.0f;
            
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Sqrt(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float result = (float)Math.Sqrt(a);
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Add(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            float result = a + b;
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Sub(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            float result = a - b;
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Mul(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            float result = a * b;
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Div(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            float result = a / b;
            if (float.IsInfinity(result)) 
                context.OpStack.PushF32(float.PositiveInfinity);
            else if (float.IsNaN(result))
                context.OpStack.PushF32(float.NaN);
            else
                context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Min(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            float result = Math.Min(a, b);
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Max(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float b = context.OpStack.PopF32();
            float result = Math.Max(a, b);
            context.OpStack.PushF32(result);
        }
        
        // Mask for the sign bit (most significant bit)
        const UInt32 F32SignMask = 0x8000_0000;
        private const UInt32 F32NotSignMask = ~F32SignMask;
        
        private static void ExecuteF32Copysign(ExecContext context)
        {
            float x = context.OpStack.PopF32();
            float y = context.OpStack.PopF32();
            // Extract raw integer bits of x and y
            UInt32 xBits = BitConverter.ToUInt32(BitConverter.GetBytes(x), 0);
            UInt32 yBits = BitConverter.ToUInt32(BitConverter.GetBytes(y), 0);
            
            // Extract the sign bit from y
            UInt32 ySign = yBits & F32SignMask;

            // Extract the magnitude bits from x
            UInt32 xMagnitude = xBits & F32NotSignMask;

            // Combine the sign of y with the magnitude of x
            UInt32 resultBits = xMagnitude | ySign;

            // Convert the result bits back to float
            float result = BitConverter.ToSingle(BitConverter.GetBytes(resultBits), 0);
            context.OpStack.PushF32(result);
        }
    }
}