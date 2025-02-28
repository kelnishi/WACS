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
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public sealed class InstF64UnOp : InstructionBase, INodeComputer<double, double>
    {
        // @Spec 3.3.1.2. f.unop
        public static readonly InstF64UnOp F64Abs      = new(OpCode.F64Abs       , ExecuteF64Abs     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly InstF64UnOp F64Neg      = new(OpCode.F64Neg       , ExecuteF64Neg     , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly InstF64UnOp F64Ceil     = new(OpCode.F64Ceil      , ExecuteF64Ceil    , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly InstF64UnOp F64Floor    = new(OpCode.F64Floor     , ExecuteF64Floor   , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly InstF64UnOp F64Trunc    = new(OpCode.F64Trunc     , ExecuteF64Trunc   , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly InstF64UnOp F64Nearest  = new(OpCode.F64Nearest   , ExecuteF64Nearest , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        public static readonly InstF64UnOp F64Sqrt     = new(OpCode.F64Sqrt      , ExecuteF64Sqrt    , NumericInst.ValidateOperands(pop: ValType.F64, push: ValType.F64));
        private readonly Func<double,double> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstF64UnOp(ByteCode op, Func<double,double> execute, NumericInst.ValidationDelegate validate) : base(op)
        {
            _execute = execute;
            _validate = validate;
        }
        public int LinkStackDiff => StackDiff;

        public Func<ExecContext, double,double> GetFunc => (_, i1) => _execute(i1);

        public override void Validate(IWasmValidationContext context) => _validate(context); // +0

        public override void Execute(ExecContext context)
        {
            double a = context.OpStack.PopF64();
            double result = _execute(a);
            context.OpStack.PushF64(result);
        }

        private static double ExecuteF64Abs(double a) => Math.Abs(a);

        private static double ExecuteF64Neg(double a) => -a;

        private static double ExecuteF64Ceil(double a)
        {
            double result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Ceiling(a)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            return result;
        }

        private static double ExecuteF64Floor(double a)
        {
            double result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Floor(a)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            return result;
        }

        private static double ExecuteF64Trunc(double a) => Math.Truncate(a);

        private static double ExecuteF64Nearest(double a)
        {
            double result = a switch {
                _ when double.IsNaN(a) => double.NaN,
                _ when double.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0 => a,
                _ => Math.Round(a, MidpointRounding.ToEven)
            };
            if (result == 0.0 && a < 0.0)
                result = -0.0;
            return result;
        }

        private static double ExecuteF64Sqrt(double a) => Math.Sqrt(a);
    }
}