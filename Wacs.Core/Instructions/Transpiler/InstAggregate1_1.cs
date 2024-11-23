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
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstAggregate1_1<TIn,TOut> : InstructionBase, ITypedValueProducer<TOut>
        where TOut : struct
    {
        private readonly Func<ExecContext, TIn> _in1;
        private readonly Func<ExecContext, TIn, TOut> _compute;
        private readonly ValType _type;

        public int CalculateSize() => Size;
        public readonly int Size;
        
        public InstAggregate1_1(ITypedValueProducer<TIn> in1, INodeComputer<TIn, TOut> compute)
        {
            _in1 = in1.GetFunc;
            _compute = compute.GetFunc;

            Size = in1.CalculateSize() + 1;

            _type = typeof(TOut).ToValType();
        }

        public TOut Run(ExecContext context) => _compute(context, _in1(context));
        
        public Func<ExecContext, TOut> GetFunc => Run;
        public override ByteCode Op => OpCode.Aggr;
        public override void Validate(IWasmValidationContext context)
        {
            throw new NotImplementedException("Validation of transpiled instructions not supported.");
        }

        public override int Execute(ExecContext context)
        {
            TOut value = Run(context);
            context.OpStack.PushValue(new Value(value));
            return Size;
        }
    }
}