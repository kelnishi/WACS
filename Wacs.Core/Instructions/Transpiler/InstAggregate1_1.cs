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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstAggregate1_1<TIn,TOut> : InstructionBase, ITypedValueProducer<TOut>
        where TOut : struct
    {
        private readonly Func<ExecContext, TIn, TOut> _compute;
        private readonly Func<ExecContext, TIn> _in1;
        private readonly Func<ExecContext, Value> _wrap;

        public InstAggregate1_1(ITypedValueProducer<TIn> in1, INodeComputer<TIn, TOut> compute)
        {
            _in1 = in1.GetFunc;
            _compute = compute.GetFunc;
            Size = in1.CalculateSize() + 1;

            if (typeof(TOut) == typeof(int)) _wrap = new WrapValueI32((ITypedValueProducer<int>)this).GetFunc;
            else if (typeof(TOut) == typeof(uint)) _wrap = new WrapValueU32((ITypedValueProducer<uint>)this).GetFunc;
            else if (typeof(TOut) == typeof(long)) _wrap = new WrapValueI64((ITypedValueProducer<long>)this).GetFunc;
            else if (typeof(TOut) == typeof(ulong)) _wrap = new WrapValueU64((ITypedValueProducer<ulong>)this).GetFunc;
            else if (typeof(TOut) == typeof(float)) _wrap = new WrapValueF32((ITypedValueProducer<float>)this).GetFunc;
            else if (typeof(TOut) == typeof(double)) _wrap = new WrapValueF64((ITypedValueProducer<double>)this).GetFunc;
            else if (typeof(TOut) == typeof(V128)) _wrap = new WrapValueV128((ITypedValueProducer<V128>)this).GetFunc;
            else if (typeof(TOut) == typeof(Value)) _wrap = new NakedValue((ITypedValueProducer<Value>)this).GetFunc;
            else throw new InvalidDataException($"Could not bind aggregate type {typeof(TOut)}");
        }

        public override ByteCode Op => WacsCode.Aggr1_1;

        public int CalculateSize() => Size;

        public Func<ExecContext, TOut> GetFunc => Run;

        public TOut Run(ExecContext context) => _compute(context, _in1(context));

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(false, "Validation of transpiled instructions not supported.");
        }

        public override void Execute(ExecContext context)
        {
            context.OpStack.PushValue(_wrap(context));
        }
    }
}