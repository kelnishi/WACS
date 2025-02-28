// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.Instructions.Numeric
{
    public sealed class InstConvert : InstructionBase
    {
        // @Spec 3.3.1.6 cvtop
        // [t1] -> [t2]

        public static readonly InstConvert I32WrapI64        = new(OpCode.I32WrapI64        , (Func<long,int>    )ExecuteI32WrapI64       , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I32));
        public static readonly InstConvert I32TruncF32S      = new(OpCode.I32TruncF32S      , (Func<float,int>   )ExecuteI32TruncF32S     , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.I32));
        public static readonly InstConvert I32TruncF32U      = new(OpCode.I32TruncF32U      , (Func<float,uint>  )ExecuteI32TruncF32U     , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.I32));
        public static readonly InstConvert I32TruncF64S      = new(OpCode.I32TruncF64S      , (Func<double,int>  )ExecuteI32TruncF64S     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.I32));
        public static readonly InstConvert I32TruncF64U      = new(OpCode.I32TruncF64U      , (Func<double,uint> )ExecuteI32TruncF64U     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.I32));
        public static readonly InstConvert I64ExtendI32S     = new(OpCode.I64ExtendI32S     , (Func<int,long>    )ExecuteI64ExtendI32S    , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I64));
        public static readonly InstConvert I64ExtendI32U     = new(OpCode.I64ExtendI32U     , (Func<uint,ulong>  )ExecuteI64ExtendI32U    , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I64));
        public static readonly InstConvert I64TruncF32S      = new(OpCode.I64TruncF32S      , (Func<float,long>  )ExecuteI64TruncF32S     , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.I64));
        public static readonly InstConvert I64TruncF32U      = new(OpCode.I64TruncF32U      , (Func<float,ulong> )ExecuteI64TruncF32U     , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.I64));
        public static readonly InstConvert I64TruncF64S      = new(OpCode.I64TruncF64S      , (Func<double,long> )ExecuteI64TruncF64S     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.I64));
        public static readonly InstConvert I64TruncF64U      = new(OpCode.I64TruncF64U      , (Func<double,ulong>)ExecuteI64TruncF64U     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.I64));
        public static readonly InstConvert F32ConvertI32S    = new(OpCode.F32ConvertI32S    , (Func<int,float>   )ExecuteF32ConvertI32S   , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.F32));
        public static readonly InstConvert F32ConvertI32U    = new(OpCode.F32ConvertI32U    , (Func<uint,float>  )ExecuteF32ConvertI32U   , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.F32));
        public static readonly InstConvert F32ConvertI64S    = new(OpCode.F32ConvertI64S    , (Func<long,float>  )ExecuteF32ConvertI64S   , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.F32));
        public static readonly InstConvert F32ConvertI64U    = new(OpCode.F32ConvertI64U    , (Func<ulong,float> )ExecuteF32ConvertI64U   , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.F32));
        public static readonly InstConvert F32DemoteF64      = new(OpCode.F32DemoteF64      , (Func<double,float>)ExecuteF32DemoteF64     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F32));
        public static readonly InstConvert F64ConvertI32S    = new(OpCode.F64ConvertI32S    , (Func<int,double>  )ExecuteF64ConvertI32S   , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.F64));
        public static readonly InstConvert F64ConvertI32U    = new(OpCode.F64ConvertI32U    , (Func<uint,double> )ExecuteF64ConvertI32U   , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.F64));
        public static readonly InstConvert F64ConvertI64S    = new(OpCode.F64ConvertI64S    , (Func<long,double> )ExecuteF64ConvertI64S   , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.F64));
        public static readonly InstConvert F64ConvertI64U    = new(OpCode.F64ConvertI64U    , (Func<ulong,double>)ExecuteF64ConvertI64U   , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.F64));
        public static readonly InstConvert F64PromoteF32     = new(OpCode.F64PromoteF32     , (Func<float,double>)ExecuteF64PromoteF32    , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F64));
        public static readonly InstConvert I32ReinterpretF32 = new(OpCode.I32ReinterpretF32 , (Func<float,int>   )ExecuteI32ReinterpretF32, NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.I32));
        public static readonly InstConvert I64ReinterpretF64 = new(OpCode.I64ReinterpretF64 , (Func<double,long> )ExecuteI64ReinterpretF64, NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.I64));
        public static readonly InstConvert F32ReinterpretI32 = new(OpCode.F32ReinterpretI32 , (Func<int,float>   )ExecuteF32ReinterpretI32, NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.F32));
        public static readonly InstConvert F64ReinterpretI64 = new(OpCode.F64ReinterpretI64 , (Func<long,double> )ExecuteF64ReinterpretI64, NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.F64));
        private readonly Executor _executor;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstConvert(ByteCode op, Delegate execute, NumericInst.ValidationDelegate validate) : base(op)
        {
            _executor = CreateExecutor(execute);
            _validate = validate;
        }

        public override void Validate(IWasmValidationContext context) => _validate(context); // +0

        public override void Execute(ExecContext context)
        {
            _executor(context);
        }

        private Executor CreateExecutor(Delegate execute)
        {
            return execute switch
            {
                Func<long, int> I64_I32 => context => context.OpStack.PushI32(I64_I32(context.OpStack.PopI64())),
                Func<float, int> F32_I32 => context => context.OpStack.PushI32(F32_I32(context.OpStack.PopF32())),
                Func<double, int> F64_I32 => context => context.OpStack.PushI32(F64_I32(context.OpStack.PopF64())),

                Func<float, uint> F32_U32 => context => context.OpStack.PushU32(F32_U32(context.OpStack.PopF32())),
                Func<double, uint> F64_U32 => context => context.OpStack.PushU32(F64_U32(context.OpStack.PopF64())),

                Func<int, long> I32_I64 => context => context.OpStack.PushI64(I32_I64(context.OpStack.PopI32())),
                Func<uint, long> U32_I64 => context => context.OpStack.PushI64(U32_I64(context.OpStack.PopU32())),
                Func<float, long> F32_I64 => context => context.OpStack.PushI64(F32_I64(context.OpStack.PopF32())),
                Func<double, long> F64_I64 => context => context.OpStack.PushI64(F64_I64(context.OpStack.PopF64())),

                Func<uint, ulong> U32_U64 => context => context.OpStack.PushU64(U32_U64(context.OpStack.PopU32())),
                Func<float, ulong> F32_U64 => context => context.OpStack.PushU64(F32_U64(context.OpStack.PopF32())),
                Func<double, ulong> F64_U64 => context => context.OpStack.PushU64(F64_U64(context.OpStack.PopF64())),

                Func<int, float> I32_F32 => context => context.OpStack.PushF32(I32_F32(context.OpStack.PopI32())),
                Func<uint, float> U32_F32 => context => context.OpStack.PushF32(U32_F32(context.OpStack.PopU32())),
                Func<long, float> I64_F32 => context => context.OpStack.PushF32(I64_F32(context.OpStack.PopI64())),
                Func<ulong, float> U64_F32 => context => context.OpStack.PushF32(U64_F32(context.OpStack.PopU64())),
                Func<double, float> F64_F32 => context => context.OpStack.PushF32(F64_F32(context.OpStack.PopF64())),

                Func<int, double> I32_D64 => context => context.OpStack.PushF64(I32_D64(context.OpStack.PopI32())),
                Func<uint, double> U32_D64 => context => context.OpStack.PushF64(U32_D64(context.OpStack.PopU32())),
                Func<long, double> I64_D64 => context => context.OpStack.PushF64(I64_D64(context.OpStack.PopI64())),
                Func<ulong, double> U64_D64 => context => context.OpStack.PushF64(U64_D64(context.OpStack.PopU64())),
                Func<float, double> F32_D64 => context => context.OpStack.PushF64(F32_D64(context.OpStack.PopF32())),
                _ => throw new InvalidDataException($"Cannot create delegate from type {execute.GetType()}")
            };
        }

        private static int ExecuteI32WrapI64(long value) => unchecked((int)value);

        private static int ExecuteI32TruncF32S(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) 
                throw new TrapException("Cannot convert NaN or infinity to integer in i32.trunc_f32_s.");
            
            double truncated = Math.Truncate(value);
        
            if (truncated is < int.MinValue or > int.MaxValue) 
                throw new TrapException("Integer overflow in i32.trunc_f32_s.");
            
            return (int)truncated;
        }

        private static uint ExecuteI32TruncF32U(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new TrapException("Cannot convert NaN or infinity to integer in i32.trunc_f32_u.");
            
            double truncated = Math.Truncate(value);
        
            if (truncated is < 0.0f or > uint.MaxValue)
                throw new TrapException("Integer overflow in i32.trunc_f32_u.");
            
            return (uint)truncated;
        }

        private static int ExecuteI32TruncF64S(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new TrapException("Cannot convert NaN or infinity to integer in i32.trunc_f64_s.");
        
            double truncated = Math.Truncate(value);
            
            if (truncated is < int.MinValue or > int.MaxValue)
                throw new TrapException("Integer overflow in i32.trunc_f64_s.");
            
            return (int)truncated;
        }

        private static uint ExecuteI32TruncF64U(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new TrapException("Cannot convert NaN or infinity to integer in i32.trunc_f64_u.");
        
            double truncated = Math.Truncate(value);
            
            if (truncated is < 0.0 or > uint.MaxValue) 
                throw new TrapException("Integer overflow in i32.trunc_f64_u.");
        
            return (uint)truncated;
        }

        private static long ExecuteI64ExtendI32S(int value) => value;

        private static ulong ExecuteI64ExtendI32U(uint value) => value;

        private static long ExecuteI64TruncF32S(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) 
                throw new TrapException("Cannot convert NaN or infinity to integer in i64.trunc_f32_s.");
            
            double truncated = Math.Truncate(value);
        
            if (truncated is > 0 and >= 9.2233720368547758E+18)
                if (decimal.Parse(truncated.ToString("G19")) > (decimal)long.MaxValue)
                    throw new TrapException("Integer overflow in i64.trunc_f32_s.");
            if (truncated is < 0 and <= -9.2233720368547758E+18)
                if (decimal.Parse(truncated.ToString("G19")) < (decimal)long.MinValue)
                    throw new TrapException("Integer overflow in i64.trunc_f32_s.");
            
            return (long)truncated;
        }

        private static ulong ExecuteI64TruncF32U(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) 
                throw new TrapException("Cannot convert NaN or infinity to integer in i64.trunc_f32_u.");
            
            double truncated = Math.Truncate(value);
            if (truncated is > 0 and >= 1.8446744073709552E+19)
                if (decimal.Parse(truncated.ToString("G20")) > (decimal)ulong.MaxValue)
                    throw new TrapException("Integer overflow in i64.trunc_f32_u.");
            if (truncated < 0.0) 
                throw new TrapException("Integer overflow in i64.trunc_f32_u.");
            
            return (ulong)truncated;
        }

        private static long ExecuteI64TruncF64S(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) 
                throw new TrapException("Cannot convert NaN or infinity to integer in i64.trunc_f64_s.");
            
            double truncated = Math.Truncate(value);
        
            if (truncated is > 0 and >= 9.2233720368547758E+18)
                if (decimal.Parse(truncated.ToString("G19")) > (decimal)long.MaxValue)
                    throw new TrapException("Integer overflow in i64.trunc_f64_s.");
            if (truncated is < 0 and <= -9.2233720368547758E+18)
                if (decimal.Parse(truncated.ToString("G19")) < (decimal)long.MinValue)
                    throw new TrapException("Integer overflow in i64.trunc_f64_s.");
            
            return (long)truncated;
        }

        private static ulong ExecuteI64TruncF64U(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) 
                throw new TrapException("Cannot convert NaN or infinity to integer in i64.trunc_f64_u.");
            
            double truncated = Math.Truncate(value);
            if (truncated is > 0 and >= 1.8446744073709552E+19)
                if (decimal.Parse(truncated.ToString("G20")) > (decimal)ulong.MaxValue)
                    throw new TrapException("Integer overflow in i64.trunc_f32_u.");
            if (truncated < 0.0) 
                throw new TrapException("Integer overflow in i64.trunc_f32_u.");
            
            return (ulong)truncated;
        }

        private static float ExecuteF32ConvertI32S(int value) => value;

        private static float ExecuteF32ConvertI32U(uint value) => value;

        private static float ExecuteF32ConvertI64S(long value) => FloatConversion.LongToFloat(value);

        private static float ExecuteF32ConvertI64U(ulong value) => FloatConversion.ULongToFloat(value);

        private static float ExecuteF32DemoteF64(double value) => (float)value;

        private static double ExecuteF64ConvertI32S(int value) => value;

        private static double ExecuteF64ConvertI32U(uint value) => value;

        private static double ExecuteF64ConvertI64S(long value) => FloatConversion.LongToDouble(value);

        private static double ExecuteF64ConvertI64U(ulong value) => FloatConversion.ULongToDouble(value);

        private static double ExecuteF64PromoteF32(float value) => value;

        private static int ExecuteI32ReinterpretF32(float value) => 
            MemoryMarshal.Cast<float, int>(MemoryMarshal.CreateSpan(ref value, 1))[0];

        private static long ExecuteI64ReinterpretF64(double value) => 
            MemoryMarshal.Cast<double, long>(MemoryMarshal.CreateSpan(ref value, 1))[0];

        private static float ExecuteF32ReinterpretI32(int value) => 
            MemoryMarshal.Cast<int, float>(MemoryMarshal.CreateSpan(ref value, 1))[0];

        private static double ExecuteF64ReinterpretI64(long value) => 
            MemoryMarshal.Cast<long, double>(MemoryMarshal.CreateSpan(ref value, 1))[0];

        delegate void Executor(ExecContext context);
    }
}