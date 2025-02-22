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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstStackProducerValue : InstructionBase, ITypedValueProducer<Value>
    {
        public InstStackProducerValue()
        {
            Size = 0;
        }

        public override ByteCode Op => WacsCode.StackVal;

        public Func<ExecContext, Value> GetFunc => FetchFromStack;

        public int CalculateSize() => 0;

        public Value FetchFromStack(ExecContext context)
        {
            //Get the type as a boxed scalar
            return context.OpStack.PopAny();
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopAny();
        }

        public override void Execute(ExecContext context) {}
    }
    
    public class InstStackProducerU32 : InstructionBase, ITypedValueProducer<uint>
    {
        public InstStackProducerU32()
        {
            Size = 0;
        }

        public override ByteCode Op => WacsCode.StackU32;

        public Func<ExecContext, uint> GetFunc => FetchFromStack;

        public int CalculateSize() => 0;

        public uint FetchFromStack(ExecContext context)
        {
            return context.OpStack.PopU32();
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopI32();
        }

        public override void Execute(ExecContext context) {}
    }
    
    public class InstStackProducerI32 : InstructionBase, ITypedValueProducer<int>
    {
        public InstStackProducerI32()
        {
            Size = 0;
        }

        public override ByteCode Op => WacsCode.StackI32;

        public Func<ExecContext, int> GetFunc => FetchFromStack;

        public int CalculateSize() => 0;

        public int FetchFromStack(ExecContext context)
        {
            return context.OpStack.PopI32();
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopI32();
        }

        public override void Execute(ExecContext context) {}
    }
}