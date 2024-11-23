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
    public class InstAggregate1_0<TIn> : InstructionBase
    {
        private readonly Func<ExecContext, TIn> _inA;
        private readonly Action<ExecContext, TIn> _compute;
        private readonly ValType _type;
        
        public int CalculateSize => Size;
        public readonly int Size;
        
        public InstAggregate1_0(ITypedValueProducer<TIn> inA, INodeComputer<TIn> compute)
        {
            _inA = inA.GetFunc;
            _compute = compute.GetFunc;

            Size = inA.CalculateSize() + 1;
        }

        public void Run(ExecContext context) => _compute(context, _inA(context));
        
        public Action<ExecContext> GetFunc => Run;
        public override ByteCode Op => OpCode.Aggr;
        public override void Validate(IWasmValidationContext context)
        {
            throw new NotImplementedException("Validation of transpiled instructions not supported.");
        }

        public override int Execute(ExecContext context)
        {
            Run(context);
            return Size;
        }
    }
    
}