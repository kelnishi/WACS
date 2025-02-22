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
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public abstract class InstI32BinOp : InstructionBase, IConstOpInstruction
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly InstI32BinOp I32Add = new Signed(OpCode.I32Add, ExecuteI32Add,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32), isConst: true);

        public static readonly InstI32BinOp I32Sub = new Signed(OpCode.I32Sub, ExecuteI32Sub,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32), isConst: true);

        public static readonly InstI32BinOp I32Mul = new Signed(OpCode.I32Mul, ExecuteI32Mul,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32), isConst: true);

        public static readonly InstI32BinOp I32DivS = new Signed(OpCode.I32DivS, ExecuteI32DivS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32DivU = new Unsigned(OpCode.I32DivU, ExecuteI32DivU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32RemS = new Signed(OpCode.I32RemS, ExecuteI32RemS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32RemU = new Unsigned(OpCode.I32RemU, ExecuteI32RemU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32And = new Unsigned(OpCode.I32And, ExecuteI32And,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Or = new Unsigned(OpCode.I32Or, ExecuteI32Or,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Xor = new Unsigned(OpCode.I32Xor, ExecuteI32Xor,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Shl = new Mixed(OpCode.I32Shl, ExecuteI32Shl,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32ShrS = new Signed(OpCode.I32ShrS, ExecuteI32ShrS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32ShrU = new Mixed(OpCode.I32ShrU, ExecuteI32ShrU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Rotl = new Mixed(OpCode.I32Rotl, ExecuteI32Rotl,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Rotr = new Mixed(OpCode.I32Rotr, ExecuteI32Rotr,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        private readonly NumericInst.ValidationDelegate _validate;


        private InstI32BinOp(ByteCode op, NumericInst.ValidationDelegate validate, bool isConst = false)
        {
            Op = op;
            _validate = validate;
            IsConstant = isConst;
        }

        public override ByteCode Op { get; }
        public override int StackDiff => -1;
        public bool IsConstant { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context); // -1

        // @Spec 4.3.2.3. iadd
        private static int ExecuteI32Add(int i1, int i2) => i1 + i2;

        // @Spec 4.3.2.4. isub
        private static int ExecuteI32Sub(int i1, int i2) => i1 - i2;

        // @Spec 4.3.2.5. imul
        private static int ExecuteI32Mul(int i1, int i2) => unchecked(i1 * i2);

        // @Spec 4.3.2.7. idiv_s
        private static int ExecuteI32DivS(int j1, int j2)
        {
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            if (j2 == -1 && j1 == int.MinValue)
                throw new TrapException("Operation results in arithmetic overflow");
            return j1 / j2;
        }

        // @Spec 4.3.2.6. idiv_u
        private static uint ExecuteI32DivU(uint i1, uint i2)
        {
            if (i2 == 0)
                throw new TrapException("Cannot divide by zero");
            return i1 / i2;
        }

        // @Spec 4.3.2.8. irem_s
        private static int ExecuteI32RemS(int j1, int j2)
        {
            if (j2 == 0)
                throw new TrapException("Cannot divide by zero");
            //Special case for arithmetic overflow
            return j2 == -1 && j1 == int.MinValue ? 0 : j1 % j2;
        }

        // @Spec 4.3.2.8. irem_u
        private static uint ExecuteI32RemU(uint i1, uint i2)
        {
            if (i2 == 0)
                throw new TrapException("Cannot divide by zero");
            return i1 % i2;
        }

        // @Spec 4.3.2.11 iand        
        private static uint ExecuteI32And(uint i1, uint i2) => i1 & i2;

        // @Spec 4.3.2.13 ior
        private static uint ExecuteI32Or(uint i1, uint i2) => i1 | i2;

        // @Spec 4.3.2.14 ixor
        private static uint ExecuteI32Xor(uint i1, uint i2) => i1 ^ i2;

        // @Spec 4.3.2.15 ishl
        private static uint ExecuteI32Shl(uint i1, int i2)
        {
            int k = i2 & 0x1F;
            return i1 << k;
        }

        // @Spec 4.3.2.17 ishr_s
        private static int ExecuteI32ShrS(int i1, int i2)
        {
            int k = i2 & 0x1F;
            return i1 >> k;
        }

        // @Spec 4.3.2.16 ishr_u
        private static uint ExecuteI32ShrU(uint i1, int i2)
        {
            int k = i2 & 0x1F;
            return i1 >> k;
        }

        // @Spec 4.3.2.18 irotl
        private static uint ExecuteI32Rotl(uint i1, int i2)
        {
            int k = i2 & 0x1F;
            uint result = i1 << k;
            if (k != 0)
                result |= i1 >> (32 - k);
            return result;
        }

        // @Spec 4.3.2.19 irotr
        private static uint ExecuteI32Rotr(uint i1, int i2)
        {
            int k = i2 & 0x1F;
            return (i1 >> k) | (i1 << (32 - k));
        }

        private sealed class Signed : InstI32BinOp, INodeComputer<int,int,int>
        {
            private readonly Func<int,int,int> _execute;

            public Signed(ByteCode op, Func<int,int,int> execute, NumericInst.ValidationDelegate validate,
                bool isConst = false) : base(op, validate, isConst) => _execute = execute;

            public Func<ExecContext, int, int, int> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                int i2 = context.OpStack.PopI32();
                int i1 = context.OpStack.PopI32();
                int result = _execute(i1, i2);
                context.OpStack.PushI32(result);
            }
        }

        private sealed class Unsigned : InstI32BinOp, INodeComputer<uint,uint,uint>
        {
            private readonly Func<uint,uint,uint> _execute;

            public Unsigned(ByteCode op, Func<uint,uint,uint> execute, NumericInst.ValidationDelegate validate,
                bool isConst = false) : base(op, validate, isConst) => _execute = execute;

            public Func<ExecContext, uint, uint, uint> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                uint i2 = context.OpStack.PopU32();
                uint i1 = context.OpStack.PopU32();
                uint result = _execute(i1, i2);
                context.OpStack.PushU32(result);
            }
        }

        private sealed class Mixed : InstI32BinOp, INodeComputer<uint,int,uint>
        {
            private readonly Func<uint,int,uint> _execute;

            public Mixed(ByteCode op, Func<uint,int,uint> execute, NumericInst.ValidationDelegate validate,
                bool isConst = false) : base(op, validate, isConst) => _execute = execute;

            public Func<ExecContext, uint, int, uint> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                int i2 = context.OpStack.PopI32();
                uint i1 = context.OpStack.PopU32();
                uint result = _execute(i1, i2);
                context.OpStack.PushU32(result);
            }
        }
    }
}