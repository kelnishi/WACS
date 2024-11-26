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

using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    public class Label : IPoolable
    {
        public int Arity;

        public InstructionPointer ContinuationAddress;

        public ByteCode Instruction;
        public int StackHeight;

        public void Clear()
        {
            Arity = default;
            Instruction = default;
            ContinuationAddress = default;
            StackHeight = default;
        }

        public bool Equals(Label other)
        {
            return StackHeight == other.StackHeight &&
                   Arity == other.Arity &&
                   Instruction.Equals(other.Instruction) &&
                   ContinuationAddress.Equals(other.ContinuationAddress);
        }

        public void Set(int arity, InstructionPointer address, ByteCode inst, int stackHeight)
        {
            if (inst.x00 != OpCode.Func && !ContinuationAddress.Equals(address))
                throw new ArgumentException("Block was entered from unknown location");
            if (StackHeight != -1)
            {
                if (StackHeight != stackHeight)
                    throw new ArgumentException("Label had different characteristics");
            }
            if (Arity != arity)
                throw new ArgumentException($"Label had different Arity: {Arity} vs {arity}");
            
            StackHeight = stackHeight;
            Arity = arity;
            Instruction = inst;
            ContinuationAddress = address;
        }
    }
}