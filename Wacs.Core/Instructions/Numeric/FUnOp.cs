using System;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // @Spec 3.3.1.2. f.unop
        public static readonly NumericInst F32Abs      = new(OpCode.F32Abs       , ExecuteF32Abs     , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Neg      = new(OpCode.F32Neg       , ExecuteF32Neg     , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Ceil     = new(OpCode.F32Ceil      , ExecuteF32Ceil    , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Floor    = new(OpCode.F32Floor     , ExecuteF32Floor   , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Trunc    = new(OpCode.F32Trunc     , ExecuteF32Trunc   , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Nearest  = new(OpCode.F32Nearest   , ExecuteF32Nearest , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly NumericInst F32Sqrt     = new(OpCode.F32Sqrt      , ExecuteF32Sqrt    , ValidateOperands(pop: ValType.F32, push: ValType.F32));
        
        public static readonly NumericInst F64Abs      = new(OpCode.F64Abs       , ExecuteF64Abs     , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Neg      = new(OpCode.F64Neg       , ExecuteF64Neg     , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Ceil     = new(OpCode.F64Ceil      , ExecuteF64Ceil    , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Floor    = new(OpCode.F64Floor     , ExecuteF64Floor   , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Trunc    = new(OpCode.F64Trunc     , ExecuteF64Trunc   , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Nearest  = new(OpCode.F64Nearest   , ExecuteF64Nearest , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly NumericInst F64Sqrt     = new(OpCode.F64Sqrt      , ExecuteF64Sqrt    , ValidateOperands(pop: ValType.F64, push: ValType.F64));
        
        
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
        
        private static void ExecuteF64Abs(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = Math.Abs(a);
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Neg(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = -a;
            context.OpStack.PushF64(result);
        }
        private static void ExecuteF64Ceil(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Ceiling(a)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Floor(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Floor(a)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Trunc(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = Math.Truncate(a);
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Nearest(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Round(a, MidpointRounding.ToEven)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            
            context.OpStack.PushF64(result);
        }

        private static void ExecuteF64Sqrt(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            var result = Math.Sqrt(a);
            context.OpStack.PushF64(result);
        }
    }
}