// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public abstract class InstI64BinOp : InstructionBase, IConstOpInstruction
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly InstI64BinOp I64Add = new Signed(OpCode.I64Add, ExecuteI64Add,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64), isConst: true);

        public static readonly InstI64BinOp I64Sub = new Signed(OpCode.I64Sub, ExecuteI64Sub,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64), isConst: true);

        public static readonly InstI64BinOp I64Mul = new Signed(OpCode.I64Mul, ExecuteI64Mul,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64), isConst: true);

        public static readonly InstI64BinOp I64DivS = new Signed(OpCode.I64DivS, ExecuteI64DivS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64DivU = new Unsigned(OpCode.I64DivU, ExecuteI64DivU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64RemS = new Signed(OpCode.I64RemS, ExecuteI64RemS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64RemU = new Unsigned(OpCode.I64RemU, ExecuteI64RemU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64And = new Unsigned(OpCode.I64And, ExecuteI64And,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64Or = new Unsigned(OpCode.I64Or, ExecuteI64Or,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64Xor = new Unsigned(OpCode.I64Xor, ExecuteI64Xor,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64Shl = new Mixed(OpCode.I64Shl, ExecuteI64Shl,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64ShrS = new Signed(OpCode.I64ShrS, ExecuteI64ShrS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64ShrU = new Mixed(OpCode.I64ShrU, ExecuteI64ShrU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64Rotl = new Mixed(OpCode.I64Rotl, ExecuteI64Rotl,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        public static readonly InstI64BinOp I64Rotr = new Mixed(OpCode.I64Rotr, ExecuteI64Rotr,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I64));

        private readonly NumericInst.ValidationDelegate _validate;

        private InstI64BinOp(ByteCode op, NumericInst.ValidationDelegate validate, bool isConst = false)
        {
            Op = op;
            _validate = validate;
            IsConstant = isConst;
        }

        public override ByteCode Op { get; }
        public bool IsConstant { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context);

        private static long ExecuteI64Add(long i1, long i2) => i1 + i2;

        private static long ExecuteI64Sub(long i1, long i2) => i1 - i2;

        private static long ExecuteI64Mul(long i1, long i2) => unchecked(i1 * i2);

        private static long ExecuteI64DivS(long j1, long j2)
        {
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            if (j2 == -1 && j1 == long.MinValue)
                throw new TrapException("Operation results in arithmetic overflow");
            return j1 / j2;
        }

        private static ulong ExecuteI64DivU(ulong j1, ulong j2)
        {
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            return j1 / j2;
        }

        private static long ExecuteI64RemS(long j1, long j2)
        {
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");

            //Special case for arithmetic overflow
            return j2 == -1 && j1 == long.MinValue ? 0 : j1 % j2;
        }

        private static ulong ExecuteI64RemU(ulong j1, ulong j2)
        {
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            return j1 % j2;
        }

        private static ulong ExecuteI64And(ulong i1, ulong i2) => i1 & i2;

        private static ulong ExecuteI64Or(ulong i1, ulong i2) => i1 | i2;

        private static ulong ExecuteI64Xor(ulong i1, ulong i2) => i1 ^ i2;

        private static ulong ExecuteI64Shl(ulong i1, long i2) => i1 << ((int)i2 & 0x3F);

        private static long ExecuteI64ShrS(long i1, long i2) => i1 >> ((int)i2 & 0x3F);

        private static ulong ExecuteI64ShrU(ulong i1, long i2) => i1 >> ((int)i2 & 0x3F);

        private static ulong ExecuteI64Rotl(ulong i1, long i2)
        {
            int k = (int)i2 & 0x3F;
            ulong result = i1 << k;
            if (k != 0)
                result |= i1 >> (64 - k);
            return result;
        }

        private static ulong ExecuteI64Rotr(ulong i1, long i2)
        {
            int k = (int)i2 & 0x3F;
            return (i1 >> k) | (i1 << (64 - k));
        }

        private sealed class Signed : InstI64BinOp, INodeComputer<long,long,long>
        {
            private readonly Func<long,long,long> _execute;

            public Signed(ByteCode op, Func<long,long,long> execute, NumericInst.ValidationDelegate validate,
                bool isConst = false) : base(op, validate, isConst) => _execute = execute;

            public Func<ExecContext, long,long,long> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                long i2 = context.OpStack.PopI64();
                long i1 = context.OpStack.PopI64();
                long result = _execute(i1, i2);
                context.OpStack.PushI64(result);
            }
        }

        private sealed class Unsigned : InstI64BinOp, INodeComputer<ulong,ulong,ulong>
        {
            private readonly Func<ulong,ulong,ulong> _execute;

            public Unsigned(ByteCode op, Func<ulong,ulong,ulong> execute, NumericInst.ValidationDelegate validate,
                bool isConst = false) : base(op, validate, isConst) => _execute = execute;

            public Func<ExecContext, ulong,ulong,ulong> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                ulong i2 = context.OpStack.PopU64();
                ulong i1 = context.OpStack.PopU64();
                ulong result = _execute(i1, i2);
                context.OpStack.PushU64(result);
            }
        }

        private sealed class Mixed : InstI64BinOp, INodeComputer<ulong,long,ulong>
        {
            private readonly Func<ulong,long,ulong> _execute;

            public Mixed(ByteCode op, Func<ulong,long,ulong> execute, NumericInst.ValidationDelegate validate,
                bool isConst = false) : base(op, validate, isConst) => _execute = execute;

            public Func<ExecContext, ulong,long,ulong> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                long i2 = context.OpStack.PopI64();
                ulong i1 = context.OpStack.PopU64();
                ulong result = _execute(i1, i2);
                context.OpStack.PushU64(result);
            }
        }
    }
}