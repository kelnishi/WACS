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
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    public class Frame : IReusable<Frame>
    {
        public SubStack<Label> Labels;
        public ModuleInstance Module { get; set; } = null!;
        public LocalsSpace Locals { get; set; } = null!;
        public InstructionPointer ContinuationAddress { get; set; } = InstructionPointer.Nil;
        public FunctionType Type { get; set; } = null!;
        public FuncIdx Index { get; set; }
        public string FuncId { get; set; } = "";

        public Label Label => Labels.Peek();
        public int Arity => (int)Type.ResultType.Length;

        public void Clear()
        {
            Labels = default;
            Module = default!;
            Locals = default!;
            ContinuationAddress = default;
            Type = default!;
            Index = default!;
            FuncId = string.Empty;
        }

        public bool Equals(Frame other)
        {
            return ReferenceEquals(this, other);
        }

        public bool Contains(LabelIdx index) =>
            index.Value < Labels.Count;

        public Label ReserveLabel() => Labels.Reserve();

        public void ForceLabels(int depth)
        {
            while (Labels.Count < depth)
            {
                var fakeLabel = ReserveLabel();
                fakeLabel.Set(ResultType.Empty, InstructionPointer.Nil, OpCode.Nop, 0);
                Labels.Push(fakeLabel);
            }

            while (Labels.Count > depth)
            {
                Labels.Pop();
            }
        }
    }
}