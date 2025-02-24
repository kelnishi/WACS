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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstLocalGetSet : InstructionBase
    {
        private readonly int _from;
        private readonly int _to;

        public InstLocalGetSet(InstLocalGet from, InstLocalSet to)
        {
            _from = from.GetIndex();
            _to = to.GetIndex();
            Size = 2;
        }

        public override ByteCode Op => WacsCode.LocalGetSet;
        public override void Validate(IWasmValidationContext context) {}

        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame.Locals.ContainsIndex(_from),
                $"Instruction local.getset could not get Local {_from}");
            context.Assert( context.Frame.Locals.ContainsIndex(_to),
                $"Instruction local.getset could not set Local {_to}");
            
            context.Frame.Locals.Span[_to] = context.Frame.Locals.Span[_from];
        }
    }

    public class InstLocalConstSet<T> : InstructionBase
    {
        private readonly Value _constantValue;
        private readonly int _to;
        private ValType type;

        public InstLocalConstSet(T c, InstLocalSet to)
        {
            _to = to.GetIndex();
            _constantValue = new Value(typeof(T).ToValType(), c!);
            Size = 2;
        }

        public override ByteCode Op => WacsCode.LocalConstSet;
        public override void Validate(IWasmValidationContext context) { }

        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame.Locals.ContainsIndex(_to),
                $"Instruction local.getset could not set Local {_to}");

            context.Frame.Locals.Span[_to] = _constantValue;
        }
    }
}