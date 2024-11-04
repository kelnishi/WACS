using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // Mask for the sign bit (most significant bit)
        private const uint F32SignMask = 0x8000_0000;
        private const uint F32NotSignMask = ~F32SignMask;

        // Mask for the sign bit (most significant bit)
        private const ulong F64SignMask = 0x8000_0000_0000_0000;

        private const ulong F64NotSignMask = ~F64SignMask;

        // @Spec 3.3.1.3. f.binop
        public static readonly NumericInst F32Add = new(OpCode.F32Add, ExecuteF32Add,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F32Sub = new(OpCode.F32Sub, ExecuteF32Sub,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F32Mul = new(OpCode.F32Mul, ExecuteF32Mul,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F32Div = new(OpCode.F32Div, ExecuteF32Div,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F32Min = new(OpCode.F32Min, ExecuteF32Min,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F32Max = new(OpCode.F32Max, ExecuteF32Max,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F32Copysign = new(OpCode.F32Copysign, ExecuteF32Copysign,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly NumericInst F64Add = new(OpCode.F64Add, ExecuteF64Add,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly NumericInst F64Sub = new(OpCode.F64Sub, ExecuteF64Sub,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly NumericInst F64Mul = new(OpCode.F64Mul, ExecuteF64Mul,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly NumericInst F64Div = new(OpCode.F64Div, ExecuteF64Div,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly NumericInst F64Min = new(OpCode.F64Min, ExecuteF64Min,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly NumericInst F64Max = new(OpCode.F64Max, ExecuteF64Max,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly NumericInst F64Copysign = new(OpCode.F64Copysign, ExecuteF64Copysign,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        private static void ExecuteF32Add(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = z1 + z2;
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Sub(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = z1 - z2;
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Mul(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = z1 * z2;
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Div(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = z1 / z2;
            if (float.IsInfinity(result))
                context.OpStack.PushF32(float.PositiveInfinity);
            else if (float.IsNaN(result))
                context.OpStack.PushF32(float.NaN);
            else
                context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Min(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = Math.Min(z1, z2);
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Max(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = Math.Max(z1, z2);
            context.OpStack.PushF32(result);
        }

        private static void ExecuteF32Copysign(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            // Extract raw integer bits of x and y
            uint xBits = BitConverter.ToUInt32(BitConverter.GetBytes(z1), 0);
            uint yBits = BitConverter.ToUInt32(BitConverter.GetBytes(z2), 0);

            // Extract the sign bit from y
            uint ySign = yBits & F32SignMask;

            // Extract the magnitude bits from x
            uint xMagnitude = xBits & F32NotSignMask;

            // Combine the sign of y with the magnitude of x
            uint resultBits = xMagnitude | ySign;

            // Convert the result bits back to float
            float result = BitConverter.ToSingle(BitConverter.GetBytes(resultBits), 0);
            context.OpStack.PushF32(result);
        }


        private static void ExecuteF64Add(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            var result = z1 + z2;
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Sub(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            var result = z1 - z2;
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Mul(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            var result = z1 * z2;
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Div(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            var result = z1 / z2;
            if (double.IsInfinity(result))
                context.OpStack.PushF64(double.PositiveInfinity);
            else if (double.IsNaN(result))
                context.OpStack.PushF64(double.NaN);
            else
                context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Min(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            var result = Math.Min(z1, z2);
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Max(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            var result = Math.Max(z1, z2);
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Copysign(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            // Extract raw integer bits of x and y
            ulong xBits = BitConverter.ToUInt64(BitConverter.GetBytes(z1), 0);
            ulong yBits = BitConverter.ToUInt64(BitConverter.GetBytes(z2), 0);

            // Extract the sign bit from y
            ulong ySign = yBits & F64SignMask;

            // Extract the magnitude bits from x
            ulong xMagnitude = xBits & F64NotSignMask;

            // Combine the sign of y with the magnitude of x
            ulong resultBits = xMagnitude | ySign;

            // Convert the result bits back to double
            double result = BitConverter.ToDouble(BitConverter.GetBytes(resultBits), 0);
            context.OpStack.PushF64(result);
        }
    }
}