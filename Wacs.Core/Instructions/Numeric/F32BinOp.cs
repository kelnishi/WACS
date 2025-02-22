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
using System.Runtime.InteropServices;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public sealed class InstF32BinOp : InstructionBase, IConstOpInstruction, INodeComputer<float,float,float>
    {
        // Mask for the sign bit (most significant bit)
        private const uint F32SignMask = 0x8000_0000;
        private const uint F32NotSignMask = ~F32SignMask;

        // @Spec 3.3.1.3. f.binop
        public static readonly InstF32BinOp F32Add = new(OpCode.F32Add, ExecuteF32Add,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32), isConst: true);

        public static readonly InstF32BinOp F32Sub = new(OpCode.F32Sub, ExecuteF32Sub,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32), isConst: true);

        public static readonly InstF32BinOp F32Mul = new(OpCode.F32Mul, ExecuteF32Mul,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32), isConst: true);

        public static readonly InstF32BinOp F32Div = new(OpCode.F32Div, ExecuteF32Div,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly InstF32BinOp F32Min = new(OpCode.F32Min, ExecuteF32Min,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly InstF32BinOp F32Max = new(OpCode.F32Max, ExecuteF32Max,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        public static readonly InstF32BinOp F32Copysign = new(OpCode.F32Copysign, ExecuteF32Copysign,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.F32));

        private readonly Func<float,float,float> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstF32BinOp(ByteCode op, Func<float,float,float> execute, NumericInst.ValidationDelegate validate, bool isConst = false)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
            IsConstant = isConst;
        }

        public override ByteCode Op { get; }
        public override int StackDiff => -1;

        public bool IsConstant { get; }

        public Func<ExecContext, float, float, float> GetFunc => (_, i1, i2) => _execute(i1, i2);

        public override void Validate(IWasmValidationContext context) => _validate(context); //-1

        public override void Execute(ExecContext context)
        {
            float z2 = context.OpStack.PopF32();
            float z1 = context.OpStack.PopF32();
            float result = _execute(z1, z2);
            context.OpStack.PushF32(result);
        }

        private static float ExecuteF32Add(float z1, float z2) => z1 + z2;

        private static float ExecuteF32Sub(float z1, float z2) => z1 - z2;

        private static float ExecuteF32Mul(float z1, float z2) => z1 * z2;

        private static float ExecuteF32Div(float z1, float z2) => z1 / z2;

        private static float ExecuteF32Min(float z1, float z2) => Math.Min(z1, z2);

        private static float ExecuteF32Max(float z1, float z2) => Math.Max(z1, z2);

        private static float ExecuteF32Copysign(float z1, float z2)
        {
            // Extract raw integer bits of x and y
            uint xBits = MemoryMarshal.Cast<float, uint>(MemoryMarshal.CreateSpan(ref z1, 1))[0];
            uint yBits = MemoryMarshal.Cast<float, uint>(MemoryMarshal.CreateSpan(ref z2, 1))[0];

            // Extract the sign bit from y
            uint ySign = yBits & F32SignMask;

            // Extract the magnitude bits from x
            uint xMagnitude = xBits & F32NotSignMask;

            // Combine the sign of y with the magnitude of x
            uint resultBits = xMagnitude | ySign;

            // Convert the result bits back to float
            return MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref resultBits, 1))[0];
        }
    }
}