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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Frame
    {
        public readonly Stack<Label> Labels = new();

        public Frame(ModuleInstance moduleInstance, FunctionType type) =>
            (Module, Type) = (moduleInstance, type);

        public ModuleInstance Module { get; }
        public LocalsSpace Locals { get; set; } = new();
        public InstructionPointer ContinuationAddress { get; set; } = InstructionPointer.Nil;

        public Label Label => Labels.Peek();

        public FunctionType Type { get; }

        public FuncIdx Index { get; set; }

        public string FuncId { get; set; } = "";

        public int Arity => (int)Type.ResultType.Length;

        //For validation
        public bool ConditionallyReachable { get; set; }

        public bool Contains(LabelIdx index) =>
            index.Value < Labels.Count;

        public void ForceLabels(int depth)
        {
            while (Labels.Count < depth)
            {
                var fakeLabel = new Label(ResultType.Empty, InstructionPointer.Nil, OpCode.Nop);
                Labels.Push(fakeLabel);
            }

            while (Labels.Count > depth)
            {
                Labels.Pop();
            }
        }
    }
}