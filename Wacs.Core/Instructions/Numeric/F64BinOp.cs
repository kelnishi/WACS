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
    public sealed class InstF64BinOp : InstructionBase, INodeComputer<double,double,double>
    {
        // Mask for the sign bit (most significant bit)
        private const ulong F64SignMask = 0x8000_0000_0000_0000;
        private const ulong F64NotSignMask = ~F64SignMask;

        // @Spec 3.3.1.3. f.binop

        public static readonly InstF64BinOp F64Add = new(OpCode.F64Add, ExecuteF64Add,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly InstF64BinOp F64Sub = new(OpCode.F64Sub, ExecuteF64Sub,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly InstF64BinOp F64Mul = new(OpCode.F64Mul, ExecuteF64Mul,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly InstF64BinOp F64Div = new(OpCode.F64Div, ExecuteF64Div,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly InstF64BinOp F64Min = new(OpCode.F64Min, ExecuteF64Min,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly InstF64BinOp F64Max = new(OpCode.F64Max, ExecuteF64Max,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        public static readonly InstF64BinOp F64Copysign = new(OpCode.F64Copysign, ExecuteF64Copysign,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.F64));

        private readonly Func<double,double,double> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstF64BinOp(ByteCode op, Func<double,double,double> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }


        public override ByteCode Op { get; }
        protected override int StackDiff => -1;

        public Func<ExecContext, double,double,double> GetFunc => (_, i1, i2) => _execute(i1, i2);

        public override void Validate(IWasmValidationContext context) => _validate(context); // -1

        public override void Execute(ExecContext context)
        {
            double z2 = context.OpStack.PopF64();
            double z1 = context.OpStack.PopF64();
            double result = _execute(z1, z2);
            context.OpStack.PushF64(result);
        }

        private static double ExecuteF64Add(double z1, double z2) => z1 + z2;

        private static double ExecuteF64Sub(double z1, double z2) => z1 - z2;

        private static double ExecuteF64Mul(double z1, double z2) => z1 * z2;

        private static double ExecuteF64Div(double z1, double z2) => z1 / z2;

        private static double ExecuteF64Min(double z1, double z2) => Math.Min(z1, z2);

        private static double ExecuteF64Max(double z1, double z2) => Math.Max(z1, z2);

        private static double ExecuteF64Copysign(double z1, double z2)
        {
            // Extract raw integer bits of x and y
            ulong xBits = MemoryMarshal.Cast<double, ulong>(MemoryMarshal.CreateSpan(ref z1, 1))[0];
            ulong yBits = MemoryMarshal.Cast<double, ulong>(MemoryMarshal.CreateSpan(ref z2, 1))[0];

            // Extract the sign bit from y
            ulong ySign = yBits & F64SignMask;

            // Extract the magnitude bits from x
            ulong xMagnitude = xBits & F64NotSignMask;

            // Combine the sign of y with the magnitude of x
            ulong resultBits = xMagnitude | ySign;

            // Convert the result bits back to float
            return MemoryMarshal.Cast<ulong, double>(MemoryMarshal.CreateSpan(ref resultBits, 1))[0];
        }
    }
}