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

using FluentValidation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public class InstFuncReturn : InstructionBase
    {
        public static InstFuncReturn Inst = new();
        
        public InstFuncReturn() : base(ByteCode.End) { }

        public override void Validate(IWasmValidationContext context)
        {
            var frame = context.PopControlFrame();
            context.OpStack.ReturnResults(frame.EndTypes);
        }

        public override void Execute(ExecContext context)
        {
            context.FunctionReturn();
        }
    }
}