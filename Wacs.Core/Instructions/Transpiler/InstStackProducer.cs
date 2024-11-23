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
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstStackProducer<T> : InstructionBase, ITypedValueProducer<T>
    where T : struct
    {
        public InstStackProducer()
        {
            _type = typeof(T).ToValType();
        }
        
        private readonly ValType _type;

        public T FetchFromStack(ExecContext context)
        {
            //Get the type as a boxed scalar
            var boxedValue = context.OpStack.PopType(_type).Scalar;
            // Unbox with Convert.ChangeType
            return (T)Convert.ChangeType(boxedValue, typeof(T));
        }

        public Func<ExecContext, T> GetFunc => FetchFromStack;

        public int CalculateSize() => 1;
        public override ByteCode Op => OpCode.StackVal;
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopType(_type);
        }

        public override int Execute(ExecContext context)
        {
            return 0;
        }
    }
}