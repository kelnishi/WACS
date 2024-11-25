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
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    public class Frame : IPoolable
    {
        public InstructionPointer ContinuationAddress = InstructionPointer.Nil;
        public string FuncId = "";

        public FuncIdx Index;
        // public ObjectPool<Label>? LabelPool = null;

        // public SubStack<Label> Labels;
        // private Stack<Label> Labels = new();
        public LocalsSpace Locals;

        // public readonly Stack<Label> Labels = new();
        public ModuleInstance Module = null!;
        public FunctionType Type = null!;

        public Label Label
        {
            get
            {
                var label = TopLabel switch
                {
                    InstBlock b => b.Label, 
                    InstLoop b => b.Label,
                    InstIf b => b.Label,
                    Expression e => e.Label,
                };
                return label;
                // return Labels.Peek();
            }
        }

        public ILabelTarget TopLabel;
        public int LabelCount = 0;
        public int StackHeight;
        
        public int Arity => Type.ResultType.Arity;

        public void Clear()
        {
            // Labels = default;
            // Labels.Clear();
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
                var fakeExpr = new Expression(FunctionType.Empty, InstructionSequence.Empty, true);
                // var fakeLabel = new Label();
                // fakeLabel.Set(ResultType.Empty, InstructionPointer.Nil, OpCode.Nop, 0);
                fakeExpr.Label.Set(ResultType.Empty, InstructionPointer.Nil, OpCode.Nop, 0);
                PushLabel(fakeExpr);
                // Labels.Push(fakeLabel);
            }

            while (LabelCount > depth)
            {
                PopLabel();
            }
        }

        public void ClearLabels()
        {
            TopLabel = default!;
            LabelCount = 0;
            // Labels.Clear();
        }
        
        public void PushLabel(ILabelTarget target)
        {
            // var label = target switch
            // {
            //     InstBlock b => b.Label, 
            //     InstLoop b => b.Label,
            //     InstIf b => b.Label,
            //     Expression e => e.Label,
            // };
            // Labels.Push(label);

            TopLabel = target;
            LabelCount += 1;
        }

        public InstructionPointer PopLabel()
        {
            // var label = Labels.Pop();
            var label = TopLabel switch
            {
                InstBlock b => b.Label, 
                InstLoop b => b.Label,
                InstIf b => b.Label,
                Expression e => e.Label,
            };
            
            var addr = label.ContinuationAddress;

            TopLabel = TopLabel.EnclosingBlock;
            
            LabelCount -= 1;
            
            return addr;
        }
    }
}