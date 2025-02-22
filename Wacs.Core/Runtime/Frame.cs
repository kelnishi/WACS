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

using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

using InstructionPointer = System.Int32;

namespace Wacs.Core.Runtime
{
    public sealed class Frame : IPoolable
    {
        public InstructionPointer ContinuationAddress = -1;
        public string FuncId = "";

        public FuncIdx Index;
        public int Head;

        public Label Label
        {
            get
            {
                if (LabelCount > 1)
                    return TopLabel!.Label;
                else
                    return ReturnLabel;
            }
        }

        private int LabelCount => TopLabel?.LabelHeight ?? 0;
        public LocalsSpace Locals;

        public ModuleInstance Module = null!;

        public Label ReturnLabel = new();
        public int StackHeight;
        public BlockTarget? TopLabel;

        public FunctionType Type = null!;

        public int Arity => Type.ResultType.Arity;

        public void Clear()
        {
            // TopLabel = default!;
            Module = default!;
            Locals = default;
            ContinuationAddress = default;
            Type = default!;
            Index = default!;
            FuncId = string.Empty;
        }

        public void ReturnLocals(ArrayPool<Value> dataPool)
        {
            dataPool.Return(Locals.Data);
            Locals = default!;
        }

        public bool Equals(Frame other)
        {
            return ReferenceEquals(this, other);
        }

        public bool Contains(LabelIdx index) =>
            index.Value < LabelCount;

        public void ForceLabels(int depth)
        {
            while (LabelCount < depth)
            {
                var fakeLabel = new InstExpressionProxy(new Label
                {
                    Arity = 0,
                    ContinuationAddress = -1,
                    Instruction = OpCode.Nop,
                    StackHeight = 0
                });
                PushLabel(fakeLabel);
            }

            while (LabelCount > depth)
            {
                PopLabels(0);
            }
        }

        public void ClearLabels()
        {
            // TopLabel = null;
        }
        
        public void SetLabel(BlockTarget baselabel)
        {
            // TopLabel = baselabel;
        }
        
        public void PushLabel(BlockTarget target)
        {
            // TopLabel = target;
        }

        public void PopLabels(int idx)
        {
            // if ((TopLabel?.LabelHeight ?? 0) <= idx + 1)
            //     throw new InvalidDataException("Label Stack underflow");

            // var Label = TopLabel;
            // for (int i = 0; i <= idx && Label != null; i++)
            // {
            //     Label = Label?.EnclosingBlock ?? null;
            // }
            //
            // Context.InstructionPointer = (Label?.Head ?? 0) + 1;
        }

        public IEnumerable<BlockTarget> EnumerateLabels()
        {
            int height = LabelCount;
            var current = TopLabel;
            while (height > 0)
            {
                yield return current;
                height -= 1;
                current = current.EnclosingBlock;
            }
        }
    }
}