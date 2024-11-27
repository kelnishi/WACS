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

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public interface IWasmValidationContext
    {
        public RuntimeAttributes Attributes { get; }

        public IValidationOpStack OpStack { get; }

        public Stack<ValidationControlFrame> ControlStack { get; }

        public FuncIdx FunctionIndex { get; }

        //Reference to the top of the control stack
        public ValidationControlFrame ControlFrame { get; }
        public ResultType ReturnType { get; }
        public bool Unreachable { get; set; }

        public TypesSpace Types { get; }
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals { get; }
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }
        public bool ContainsLabel(uint label);
        public void PushControlFrame(ByteCode opCode, FunctionType types);
        public ValidationControlFrame PopControlFrame();
        public void SetUnreachable();
        public void Assert(bool factIsTrue, string formatString, params object[] args);
        public void Assert([NotNull] object? objIsNotNull, string formatString, params object[] args);
        public void ValidateBlock(Block instructionBlock, int index = 0);
    }
}