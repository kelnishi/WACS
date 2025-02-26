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
    public abstract class InstFusedI64Const : InstructionBase, ITypedValueProducer<long>
    {
        static readonly NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64);
        protected readonly Func<ExecContext, long> _previous;
        protected long _constant;
        protected Func<ExecContext, long> _execute = null!;

        protected InstFusedI64Const(ITypedValueProducer<long> prev, long constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, long> GetFunc => _execute;

        public override void Validate(IWasmValidationContext context) => _validate(context);
        public sealed override int StackDiff => +1;

        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI64(_execute(context));
        }
    }

    public class InstFusedI64Add : InstFusedI64Const
    {
        public InstFusedI64Add(ITypedValueProducer<long> prev, long constant) : base(prev, constant) => 
            _execute = context => _previous(context) + _constant;

        public override ByteCode Op => ByteCode.I64FusedAdd;
    }
    
    public class InstFusedI64Sub : InstFusedI64Const
    {
        public InstFusedI64Sub(ITypedValueProducer<long> prev, long constant) : base(prev, constant) => 
            _execute = context => _previous(context) - _constant;

        public override ByteCode Op => ByteCode.I64FusedSub;
    }
    
    public class InstFusedI64Mul : InstFusedI64Const
    {
        public InstFusedI64Mul(ITypedValueProducer<long> prev, long constant) : base(prev, constant) => 
            _execute = context => _previous(context) * _constant;

        public override ByteCode Op => ByteCode.I64FusedMul;
    }
    
    public abstract class InstFusedU64Const : InstructionBase, ITypedValueProducer<ulong>
    {
        static readonly NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64);
        protected readonly Func<ExecContext, ulong> _previous;
        protected ulong _constant;
        protected Func<ExecContext, ulong> _execute = null!;

        protected InstFusedU64Const(ITypedValueProducer<ulong> prev, ulong constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, ulong> GetFunc => _execute;

        public override void Validate(IWasmValidationContext context) => _validate(context);
        public sealed override int StackDiff => +1;

        public override void Execute(ExecContext context)
        {
            context.OpStack.PushU64(_execute(context));
        }
    }
    
    public class InstFusedU64And : InstFusedU64Const
    {
        public InstFusedU64And(ITypedValueProducer<ulong> prev, ulong constant) : base(prev, constant) => 
            _execute = context => _previous(context) & _constant;

        public override ByteCode Op => ByteCode.I64FusedAnd;
    }
    
    public class InstFusedU64Or : InstFusedU64Const
    {
        public InstFusedU64Or(ITypedValueProducer<ulong> prev, ulong constant) : base(prev, constant) => 
            _execute = context => _previous(context) | _constant;

        public override ByteCode Op => ByteCode.I64FusedOr;
    }
}