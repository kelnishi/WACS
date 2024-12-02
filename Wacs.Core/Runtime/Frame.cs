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

using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    public sealed class Frame : IPoolable
    {
        public InstructionPointer ContinuationAddress = InstructionPointer.Nil;
        public string FuncId = "";

        public FuncIdx Index;

        public Label Label;
        public int LabelCount = 0;
        public LocalsSpace Locals;

        public ModuleInstance Module = null!;

        public Label ReturnLabel = new();
        public int StackHeight;
        public BlockTarget TopLabel;
        public FunctionType Type = null!;

        public int Arity => Type.ResultType.Arity;

        public void Clear()
        {
            TopLabel = default!;
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
                    ContinuationAddress = InstructionPointer.Nil,
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
            TopLabel = default!;
            LabelCount = 0;
        }

        public void PushLabel(BlockTarget target)
        {
            TopLabel = target;
            LabelCount += 1;

            Label = LabelCount > 1
                ? TopLabel.Label 
                : ReturnLabel;
        }

        public InstructionPointer PopLabels(int idx)
        {
            if (LabelCount <= idx + 1)
                throw new InvalidDataException("Label Stack underflow");
            
            BlockTarget oldLabel;
            do
            {
                oldLabel = TopLabel;
                TopLabel = TopLabel.EnclosingBlock;
                idx -= 1;
                LabelCount -= 1;
            } while (idx >= 0);

            Label = LabelCount > 1
                ? TopLabel.Label 
                : ReturnLabel;

            return oldLabel.Label.ContinuationAddress;
        }

        public IEnumerable<Label> EnumerateLabels()
        {
            int height = LabelCount;
            var current = TopLabel;
            while (height > 0)
            {
                yield return current.Label;
                height -= 1;
                current = current.EnclosingBlock;
            }
        }
    }
}