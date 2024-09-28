using System;
using Wacs.Core.Execution;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32WrapI64        = new NumericInst(OpCode.I32WrapI64        , ExecuteI32WrapI64       );        
        public static readonly NumericInst I32TruncF32S      = new NumericInst(OpCode.I32TruncF32S      , ExecuteI32TruncF32S     );      
        public static readonly NumericInst I32TruncF32U      = new NumericInst(OpCode.I32TruncF32U      , ExecuteI32TruncF32U     );      
        public static readonly NumericInst I32TruncF64S      = new NumericInst(OpCode.I32TruncF64S      , ExecuteI32TruncF64S     );      
        public static readonly NumericInst I32TruncF64U      = new NumericInst(OpCode.I32TruncF64U      , ExecuteI32TruncF64U     );      
        public static readonly NumericInst I64ExtendI32S     = new NumericInst(OpCode.I64ExtendI32S     , ExecuteI64ExtendI32S    );     
        public static readonly NumericInst I64ExtendI32U     = new NumericInst(OpCode.I64ExtendI32U     , ExecuteI64ExtendI32U    );     
        public static readonly NumericInst I64TruncF32S      = new NumericInst(OpCode.I64TruncF32S      , ExecuteI64TruncF32S     );      
        public static readonly NumericInst I64TruncF32U      = new NumericInst(OpCode.I64TruncF32U      , ExecuteI64TruncF32U     );      
        public static readonly NumericInst I64TruncF64S      = new NumericInst(OpCode.I64TruncF64S      , ExecuteI64TruncF64S     );      
        public static readonly NumericInst I64TruncF64U      = new NumericInst(OpCode.I64TruncF64U      , ExecuteI64TruncF64U     );      
        public static readonly NumericInst F32ConvertI32S    = new NumericInst(OpCode.F32ConvertI32S    , ExecuteF32ConvertI32S   );    
        public static readonly NumericInst F32ConvertI32U    = new NumericInst(OpCode.F32ConvertI32U    , ExecuteF32ConvertI32U   );    
        public static readonly NumericInst F32ConvertI64S    = new NumericInst(OpCode.F32ConvertI64S    , ExecuteF32ConvertI64S   );    
        public static readonly NumericInst F32ConvertI64U    = new NumericInst(OpCode.F32ConvertI64U    , ExecuteF32ConvertI64U   );    
        public static readonly NumericInst F32DemoteF64      = new NumericInst(OpCode.F32DemoteF64      , ExecuteF32DemoteF64     );      
        public static readonly NumericInst F64ConvertI32S    = new NumericInst(OpCode.F64ConvertI32S    , ExecuteF64ConvertI32S   );    
        public static readonly NumericInst F64ConvertI32U    = new NumericInst(OpCode.F64ConvertI32U    , ExecuteF64ConvertI32U   );    
        public static readonly NumericInst F64ConvertI64S    = new NumericInst(OpCode.F64ConvertI64S    , ExecuteF64ConvertI64S   );    
        public static readonly NumericInst F64ConvertI64U    = new NumericInst(OpCode.F64ConvertI64U    , ExecuteF64ConvertI64U   );    
        public static readonly NumericInst F64PromoteF32     = new NumericInst(OpCode.F64PromoteF32     , ExecuteF64PromoteF32    );     
        public static readonly NumericInst I32ReinterpretF32 = new NumericInst(OpCode.I32ReinterpretF32 , ExecuteI32ReinterpretF32); 
        public static readonly NumericInst I64ReinterpretF64 = new NumericInst(OpCode.I64ReinterpretF64 , ExecuteI64ReinterpretF64); 
        public static readonly NumericInst F32ReinterpretI32 = new NumericInst(OpCode.F32ReinterpretI32 , ExecuteF32ReinterpretI32); 
        public static readonly NumericInst F64ReinterpretI64 = new NumericInst(OpCode.F64ReinterpretI64 , ExecuteF64ReinterpretI64);

        private static void ExecuteI32WrapI64(ExecContext context)
        {
            long value = context.Stack.PopI64();
            int result = unchecked((int)value);
            context.Stack.PushI32(result);
        }
        
        private static void ExecuteI32TruncF32S(ExecContext context)
        {
            float value = context.Stack.PopF32();
            if (float.IsNaN(value) || float.IsInfinity(value)) 
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i32.trunc_f32_s.");
            
            float truncated = (float)Math.Truncate(value);

            if (truncated < int.MinValue || truncated > int.MaxValue) 
                throw new OverflowException("Integer overflow in i32.trunc_f32_s.");
            
            int result = (int)truncated;
            context.Stack.PushI32(result);
        }

        private static void ExecuteI32TruncF32U(ExecContext context)
        {
            float value = context.Stack.PopF32();
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i32.trunc_f32_u.");
            
            float truncated = (float)Math.Truncate(value);

            if (truncated < 0.0f || truncated > uint.MaxValue)
                throw new OverflowException("Integer overflow in i32.trunc_f32_u.");
            
            uint result = (uint)truncated;

            context.Stack.PushI32(result);
        }

        private static void ExecuteI32TruncF64S(ExecContext context)
        {
            double value = context.Stack.PopF64();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i32.trunc_f64_s.");

            double truncated = Math.Truncate(value);
            
            if (truncated < int.MinValue || truncated > int.MaxValue)
                throw new OverflowException("Integer overflow in i32.trunc_f64_s.");
            
            int result = (int)truncated;
            context.Stack.PushI32(result);
        }

        private static void ExecuteI32TruncF64U(ExecContext context)
        {
            double value = context.Stack.PopF64();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i32.trunc_f64_u.");

            double truncated = Math.Truncate(value);
            
            if (truncated < 0.0 || truncated > uint.MaxValue) 
                throw new OverflowException("Integer overflow in i32.trunc_f64_u.");

            uint result = (uint)truncated;
            context.Stack.PushI32(result);
        }

        private static void ExecuteI64ExtendI32S(ExecContext context)
        {
            int value = context.Stack.PopI32();
            long result = value;
            context.Stack.PushI64(result);
        }

        private static void ExecuteI64ExtendI32U(ExecContext context)
        {
            uint value = context.Stack.PopI32();
            ulong result = value;
            context.Stack.PushI64(result);
        }

        private static void ExecuteI64TruncF32S(ExecContext context)
        {
            float value = context.Stack.PopF32();
            if (float.IsNaN(value) || float.IsInfinity(value)) 
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i64.trunc_f32_s.");
            
            float truncated = (float)Math.Truncate(value);

            if (truncated < long.MinValue || truncated > long.MaxValue) 
                throw new OverflowException("Integer overflow in i64.trunc_f32_s.");
            
            long result = (long)truncated;
            context.Stack.PushI64(result);
        }

        private static void ExecuteI64TruncF32U(ExecContext context)
        {
            float value = context.Stack.PopF32();
            if (float.IsNaN(value) || float.IsInfinity(value)) 
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i64.trunc_f32_u.");
            
            float truncated = (float)Math.Truncate(value);

            if (truncated < 0.0 || truncated > ulong.MaxValue) 
                throw new OverflowException("Integer overflow in i64.trunc_f32_u.");
            
            ulong result = (ulong)truncated;
            context.Stack.PushI64(result);
        }

        private static void ExecuteI64TruncF64S(ExecContext context)
        {
            double value = context.Stack.PopF64();
            if (double.IsNaN(value) || double.IsInfinity(value)) 
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i64.trunc_f64_s.");
            
            double truncated = Math.Truncate(value);

            if (truncated < long.MinValue || truncated > long.MaxValue) 
                throw new OverflowException("Integer overflow in i64.trunc_f64_s.");
            
            long result = (long)truncated;
            context.Stack.PushI64(result);
        }

        private static void ExecuteI64TruncF64U(ExecContext context)
        {
            double value = context.Stack.PopF64();
            if (double.IsNaN(value) || double.IsInfinity(value)) 
                throw new InvalidOperationException("Cannot convert NaN or infinity to integer in i64.trunc_f64_u.");
            
            double truncated = Math.Truncate(value);

            if (truncated < 0.0 || truncated > ulong.MaxValue) 
                throw new OverflowException("Integer overflow in i64.trunc_f64_u.");
            
            ulong result = (ulong)truncated;
            context.Stack.PushI64(result);
        }

        private static void ExecuteF32ConvertI32S(ExecContext context)
        {
            int value = context.Stack.PopI32();
            float result = (float)value;
            context.Stack.PushF32(result);
        }

        private static void ExecuteF32ConvertI32U(ExecContext context)
        {
            uint value = context.Stack.PopI32();
            float result = value;
            context.Stack.PushF32(result);
        }

        private static void ExecuteF32ConvertI64S(ExecContext context)
        {
            long value = context.Stack.PopI64();
            float result = value;
            context.Stack.PushF32(result);
        }

        private static void ExecuteF32ConvertI64U(ExecContext context)
        {
            ulong value = context.Stack.PopI64();
            float result = value;
            context.Stack.PushF32(result);
        }

        private static void ExecuteF32DemoteF64(ExecContext context)
        {
            double value = context.Stack.PopF64();
            float result = (float)value;
            context.Stack.PushF32(result);
        }

        private static void ExecuteF64ConvertI32S(ExecContext context)
        {
            int value = context.Stack.PopI32();
            double result = value;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64ConvertI32U(ExecContext context)
        {
            uint value = context.Stack.PopI32();
            double result = value;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64ConvertI64S(ExecContext context)
        {
            long value = context.Stack.PopI64();
            double result = value;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64ConvertI64U(ExecContext context)
        {
            ulong value = context.Stack.PopI64();
            double result = value;
            context.Stack.PushF64(result);
        }

        private static void ExecuteF64PromoteF32(ExecContext context)
        {
            float value = context.Stack.PopF32();
            double result = value;
            context.Stack.PushF64(result);
        }

        private static void ExecuteI32ReinterpretF32(ExecContext context)
        {
            float value = context.Stack.PopF32();
            byte[] bytes = BitConverter.GetBytes(value);
            int result = BitConverter.ToInt32(bytes, 0);
            context.Stack.PushI32(result);
        }

        private static void ExecuteI64ReinterpretF64(ExecContext context)
        {
            double value = context.Stack.PopF64();
            byte[] bytes = BitConverter.GetBytes(value);
            long result = BitConverter.ToInt64(bytes, 0);
            context.Stack.PushI64(result);
        }

        private static void ExecuteF32ReinterpretI32(ExecContext context)
        {
            int value = context.Stack.PopI32();
            byte[] bytes = BitConverter.GetBytes(value);
            float result = BitConverter.ToSingle(bytes, 0);
            context.Stack.PushF32(result);
        }

        private static void ExecuteF64ReinterpretI64(ExecContext context)
        {
            long value = context.Stack.PopI64();
            byte[] bytes = BitConverter.GetBytes(value);
            double result = BitConverter.ToSingle(bytes, 0);
            context.Stack.PushF64(result);
        }
        
    }
}