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
    public sealed class InstF32UnOp : InstructionBase, IConstOpInstruction, INodeComputer<float, float>
    {
        // @Spec 3.3.1.2. f.unop
        public static readonly InstF32UnOp F32Abs      = new(OpCode.F32Abs       , ExecuteF32Abs     , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly InstF32UnOp F32Neg      = new(OpCode.F32Neg       , ExecuteF32Neg     , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly InstF32UnOp F32Ceil     = new(OpCode.F32Ceil      , ExecuteF32Ceil    , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly InstF32UnOp F32Floor    = new(OpCode.F32Floor     , ExecuteF32Floor   , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly InstF32UnOp F32Trunc    = new(OpCode.F32Trunc     , ExecuteF32Trunc   , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly InstF32UnOp F32Nearest  = new(OpCode.F32Nearest   , ExecuteF32Nearest , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        public static readonly InstF32UnOp F32Sqrt     = new(OpCode.F32Sqrt      , ExecuteF32Sqrt    , NumericInst.ValidateOperands(pop: ValType.F32, push: ValType.F32));
        private readonly Func<float,float> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstF32UnOp(ByteCode op, Func<float,float> execute, NumericInst.ValidationDelegate validate, bool isConst = false)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
            IsConstant = isConst;
        }

        public override ByteCode Op { get; }

        public bool IsConstant { get; }

        public Func<ExecContext, float,float> GetFunc => (_, i1) => _execute(i1);

        public override void Validate(IWasmValidationContext context) => _validate(context); // +0

        public override void Execute(ExecContext context)
        {
            float a = context.OpStack.PopF32();
            float result = _execute(a);
            context.OpStack.PushF32(result);
        }

        private static float ExecuteF32Abs(float a) => Math.Abs(a);
        private static float ExecuteF32Neg(float a) => -a;

        private static float ExecuteF32Ceil(float a)
        {
            float result = a switch {
                _ when float.IsNaN(a) => float.NaN,
                _ when float.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0f => a,
                _ => (float)Math.Ceiling(a)
            };
            if (result == 0.0f && a < 0.0f)
                result = -0.0f;
            return result;
        }

        private static float ExecuteF32Floor(float a)
        {
            float result = a switch {
                _ when float.IsNaN(a) => float.NaN,
                _ when float.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0f => a,
                _ => (float)Math.Floor(a)
            };
            if (result == 0.0f && a < 0.0f)
                result = -0.0f;
            return result;
        }

        private static float ExecuteF32Trunc(float a) => (float)Math.Truncate(a);

        private static float ExecuteF32Nearest(float a)
        {
            float result = a switch {
                _ when float.IsNaN(a) => float.NaN,
                _ when float.IsInfinity(a) => a,
                _ when Math.Abs(a) == 0.0f => a,
                _ => (float)Math.Round(a, MidpointRounding.ToEven)
            };
            if (result == 0.0f && a < 0.0f)
                result = -0.0f;
            return result;
        }

        private static float ExecuteF32Sqrt(float a) => (float)Math.Sqrt(a);
    }
}