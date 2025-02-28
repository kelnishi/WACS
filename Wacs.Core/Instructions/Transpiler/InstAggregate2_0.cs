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
    public class InstAggregate2_0<TIn1,TIn2> : InstructionBase
    {
        private readonly Action<ExecContext, TIn1, TIn2> _compute;
        private readonly Func<ExecContext, TIn1> _in1;
        private readonly Func<ExecContext, TIn2> _in2;
        public int LinkStackDiff { get; set; }
        private List<InstructionBase> linkDependents = new();

        public InstAggregate2_0(ITypedValueProducer<TIn1> in1, ITypedValueProducer<TIn2> in2, INodeConsumer<TIn1,TIn2> compute) : base(ByteCode.Aggr2_0)
        {
            _in1 = in1.GetFunc;
            _in2 = in2.GetFunc;
            
            LinkStackDiff = Math.Min(0, in1.LinkStackDiff) + Math.Min(0, in2.LinkStackDiff);
            
            if (compute is IComplexLinkBehavior) 
                linkDependents.Add((compute as InstructionBase)!);
            
            _compute = compute.GetFunc;

            Size = in1.CalculateSize() + in2.CalculateSize() + 1;
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(false, "Validation of transpiled instructions not supported.");
        }
        
        public override InstructionBase Link(ExecContext context, int pointer)
        {
            context.LinkOpStackHeight += LinkStackDiff;
            
            int stack = context.LinkOpStackHeight;
            
            foreach (var dependent in linkDependents)
                dependent.Link(context, pointer);
            
            context.LinkOpStackHeight = stack;
            return this;
        }

        public override void Execute(ExecContext context)
        {
            _compute(context, _in1(context), _in2(context));
        }
    }
    
}