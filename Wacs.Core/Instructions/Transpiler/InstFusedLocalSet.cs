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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstLocalGetSet : InstructionBase
    {
        private int _from;
        private int _to;
        public InstLocalGetSet(InstLocalGet from, InstLocalSet to)
        {
            _from = from.GetIndex().Value;
            _to = to.GetIndex().Value;
            Size = 2;
        }
        public override ByteCode Op => WacsCode.LocalGetSet;
        public override void Validate(IWasmValidationContext context) {}
        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame.Locals.Contains((LocalIdx)_from),
                $"Instruction local.getset could not get Local {_from}");
            context.Assert( context.Frame.Locals.Contains((LocalIdx)_to),
                $"Instruction local.getset could not set Local {_to}");
            
            context.Frame.Locals.Data[_to] = context.Frame.Locals.Data[_from];
        }
    }

    public class InstLocalConstSet<T> : InstructionBase
    {
        private Value _constantValue;
        private ValType type;
        private int _to;
        
        public InstLocalConstSet(T c, InstLocalSet to)
        {
            _to = to.GetIndex().Value;
            _constantValue = new Value(typeof(T).ToValType(), c!);
            Size = 2;
        }
        public override ByteCode Op => WacsCode.LocalConstSet;
        public override void Validate(IWasmValidationContext context) { }

        public override void Execute(ExecContext context)
        {
            context.Assert( context.Frame.Locals.Contains((LocalIdx)_to),
                $"Instruction local.getset could not set Local {_to}");

            context.Frame.Locals.Data[_to] = _constantValue;
        }
    }
}