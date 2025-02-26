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
    public abstract class InstFusedI32Const : InstructionBase, ITypedValueProducer<int>
    {
        static readonly NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32);
        protected readonly Func<ExecContext, int> _previous;
        protected int _constant;
        protected Func<ExecContext, int> _execute = null!;

        public int LinkStackDiff => StackDiff;
        
        protected InstFusedI32Const(ByteCode op, ITypedValueProducer<int> prev, int constant) : base(op, +1)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, int> GetFunc => _execute;

        public override void Validate(IWasmValidationContext context) => _validate(context);

        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI32(_execute(context));
        }
    }

    public class InstFusedI32Add : InstFusedI32Const
    {
        public InstFusedI32Add(ITypedValueProducer<int> prev, int constant) : base(ByteCode.I32FusedAdd, prev, constant) => 
            _execute = context => _previous(context) + _constant;
    }
    
    public class InstFusedI32Sub : InstFusedI32Const
    {
        public InstFusedI32Sub(ITypedValueProducer<int> prev, int constant) : base(ByteCode.I32FusedSub, prev, constant) => 
            _execute = context => _previous(context) - _constant;
    }
    
    public class InstFusedI32Mul : InstFusedI32Const
    {
        public InstFusedI32Mul(ITypedValueProducer<int> prev, int constant) : base(ByteCode.I32FusedMul, prev, constant) => 
            _execute = context => _previous(context) * _constant;
    }
    
    public abstract class InstFusedU32Const : InstructionBase, ITypedValueProducer<uint>
    {
        static readonly NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32);
        protected readonly Func<ExecContext, uint> _previous;
        protected uint _constant;
        protected Func<ExecContext, uint> _execute = null!;

        public int LinkStackDiff => StackDiff;

        protected InstFusedU32Const(ByteCode op, ITypedValueProducer<uint> prev, uint constant) : base(op, +1)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, uint> GetFunc => _execute;

        public override void Validate(IWasmValidationContext context) => _validate(context);
        
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushU32(_execute(context));
        }
    }
    
    public class InstFusedU32And : InstFusedU32Const
    {
        public InstFusedU32And(ITypedValueProducer<uint> prev, uint constant) : base(ByteCode.I32FusedAnd, prev, constant) => 
            _execute = context => _previous(context) & _constant;
    }
    
    public class InstFusedU32Or : InstFusedU32Const
    {
        public InstFusedU32Or(ITypedValueProducer<uint> prev, uint constant) : base(ByteCode.I32FusedOr, prev, constant) => 
            _execute = context => _previous(context) | _constant;
    }
}