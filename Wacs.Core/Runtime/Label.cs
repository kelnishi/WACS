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
using InstructionPointer = System.Int32;


namespace Wacs.Core.Runtime
{
    public class Label
    {
        public int Arity;
        public InstructionPointer ContinuationAddress;
        public ByteCode Instruction;
        public int Parameters;
        public int Results;
        public int StackHeight;

        public Label()
        {
        }

        public Label(Label copy)
        {
            Arity = copy.Arity;
            Instruction = copy.Instruction;
            StackHeight = copy.StackHeight;
        }

        public bool Equals(Label other)
        {
            return StackHeight == other.StackHeight &&
                   Arity == other.Arity &&
                   Instruction.Equals(other.Instruction) &&
                   ContinuationAddress.Equals(other.ContinuationAddress);
        }

        public override string ToString()
        {
            return $"Label(Instruction: {Instruction}, Arity: {Arity}, StackHeight: {StackHeight}, ContinuationAddress: {ContinuationAddress})";
        }
    }
}