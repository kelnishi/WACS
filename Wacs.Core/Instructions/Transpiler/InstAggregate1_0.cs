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
using System.Collections.Generic;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstAggregate1_0<TIn> : InstructionBase
    {
        private readonly Action<ExecContext, TIn> _compute;
        private readonly Func<ExecContext, TIn> _inA;
        public sealed override int StackDiff { get; set; }
        private List<InstructionBase> linkDependents = new();
        
        public InstAggregate1_0(ITypedValueProducer<TIn> inA, INodeConsumer<TIn> consumer)
        {
            _inA = inA.GetFunc;
            StackDiff = Math.Min(0, inA.StackDiff);

            if (consumer is IComplexLinkBehavior) 
                linkDependents.Add((consumer as InstructionBase)!);
            
            _compute = consumer.GetFunc;

            Size = inA.CalculateSize() + 1;
        }

        public override ByteCode Op => ByteCode.Aggr1_0;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(false, "Validation of transpiled instructions not supported.");
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            _ = base.Link(context, pointer);
            int stack = context.LinkOpStackHeight;
            
            foreach (var dependent in linkDependents)
                dependent.Link(context, pointer);
            
            context.LinkOpStackHeight = stack;
            return this;
        }

        public override void Execute(ExecContext context)
        {
            _compute(context, _inA(context));
        }
    }
    
}