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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public class InstI32BinOp : InstructionBase, IConstOpInstruction
    {
        // @Spec 3.3.1.3. i.binop
        public static readonly InstI32BinOp I32Add = new(OpCode.I32Add, (Func<int,int,int>)ExecuteI32Add,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32), isConst: true);

        public static readonly InstI32BinOp I32Sub = new(OpCode.I32Sub, (Func<int,int,int>)ExecuteI32Sub,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32), isConst: true);

        public static readonly InstI32BinOp I32Mul = new(OpCode.I32Mul, (Func<int,int,int>)ExecuteI32Mul,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32), isConst: true);

        public static readonly InstI32BinOp I32DivS = new(OpCode.I32DivS, (Func<int,int,int>)ExecuteI32DivS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32DivU = new(OpCode.I32DivU, (Func<uint,uint,uint>)ExecuteI32DivU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32RemS = new(OpCode.I32RemS, (Func<int,int,int>)ExecuteI32RemS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32RemU = new(OpCode.I32RemU, (Func<uint,uint,uint>)ExecuteI32RemU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32And = new(OpCode.I32And, (Func<uint,uint,uint>)ExecuteI32And,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Or = new(OpCode.I32Or, (Func<uint,uint,uint>)ExecuteI32Or,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Xor = new(OpCode.I32Xor, (Func<uint,uint,uint>)ExecuteI32Xor,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Shl = new(OpCode.I32Shl, (Func<uint,int,uint>)ExecuteI32Shl,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32ShrS = new(OpCode.I32ShrS, (Func<int,int,int>)ExecuteI32ShrS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32ShrU = new(OpCode.I32ShrU, (Func<uint,int,uint>)ExecuteI32ShrU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Rotl = new(OpCode.I32Rotl, (Func<uint,int,uint>)ExecuteI32Rotl,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32BinOp I32Rotr = new(OpCode.I32Rotr, (Func<uint,int,uint>)ExecuteI32Rotr,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public override ByteCode Op { get; }
        private readonly Delegate _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private readonly bool _isConst;
        public bool IsConstant => _isConst;
        
        private InstI32BinOp(ByteCode op, Delegate execute, NumericInst.ValidationDelegate validate, bool isConst = false)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
            _isConst = isConst;
        }
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        public override int Execute(ExecContext context)
        {
            switch (_execute)
            {
                case Func<int,int,int> signed:
                {
                    int i2 = context.OpStack.PopI32();
                    int i1 = context.OpStack.PopI32();
                    int result = signed(i1, i2);
                    context.OpStack.PushI32(result);
                    break;
                }
                case Func<uint,uint,uint> unsigned:
                {
                    uint i2 = context.OpStack.PopU32();
                    uint i1 = context.OpStack.PopU32();
                    uint result = unsigned(i1, i2);
                    context.OpStack.PushU32(result);
                    break;
                }
                case Func<uint,int,uint> mixed:
                {
                    int i2 = context.OpStack.PopI32();
                    uint i1 = context.OpStack.PopU32();
                    uint result = mixed(i1, i2);
                    context.OpStack.PushU32(result);
                    break;
                }
            }
            return 1;
        }

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
            int k = i2 & 31;
            return (i1 >> k) | (i1 << (32 - k));
        }
    }
}