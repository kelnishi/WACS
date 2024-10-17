using System;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.3. f.binop
        public static readonly NumericInst F32Add      = new NumericInst(OpCode.F32Add       , ExecuteF32Add      , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Sub      = new NumericInst(OpCode.F32Sub       , ExecuteF32Sub      , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Mul      = new NumericInst(OpCode.F32Mul       , ExecuteF32Mul      , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Div      = new NumericInst(OpCode.F32Div       , ExecuteF32Div      , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Min      = new NumericInst(OpCode.F32Min       , ExecuteF32Min      , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Max      = new NumericInst(OpCode.F32Max       , ExecuteF32Max      , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Copysign = new NumericInst(OpCode.F32Copysign  , ExecuteF32Copysign , ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));
        
        public static readonly NumericInst F64Add      = new NumericInst(OpCode.F64Add       , ExecuteF64Add      , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Sub      = new NumericInst(OpCode.F64Sub       , ExecuteF64Sub      , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Mul      = new NumericInst(OpCode.F64Mul       , ExecuteF64Mul      , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Div      = new NumericInst(OpCode.F64Div       , ExecuteF64Div      , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Min      = new NumericInst(OpCode.F64Min       , ExecuteF64Min      , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Max      = new NumericInst(OpCode.F64Max       , ExecuteF64Max      , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Copysign = new NumericInst(OpCode.F64Copysign  , ExecuteF64Copysign , ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

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
        
        
        private static void ExecuteF64Add(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            var result = a + b;
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Sub(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            var result = a - b;
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Mul(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            var result = a * b;
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Div(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            var result = a / b;
            if (double.IsInfinity(result)) 
                context.OpStack.PushF64(double.PositiveInfinity);
            else if (double.IsNaN(result))
                context.OpStack.PushF64(double.NaN);
            else
                context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Min(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            var result = Math.Min(a, b);
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Max(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double b = context.OpStack.PopF64();
            var result = Math.Max(a, b);
            context.OpStack.PushF64(result);
        }
        
        // Mask for the sign bit (most significant bit)
        private const UInt64 F64SignMask = 0x8000_0000_0000_0000;
        private const UInt64 F64NotSignMask = ~F64SignMask;
        
        private static void ExecuteF64Copysign(ExecContext context)
        {
            double x = context.OpStack.PopF64();
            double y = context.OpStack.PopF64();
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
            context.OpStack.PushF64(result);
        }
    }
}