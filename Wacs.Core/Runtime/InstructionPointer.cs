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

namespace Wacs.Core.Runtime
{
    public readonly struct InstructionPointer : IEquatable<InstructionPointer>
    {
        public readonly InstructionSequence Sequence;
        public readonly int Index;

        public InstructionPointer(InstructionSequence seq, int index)
        {
            Sequence = seq;
            Index = index;
        }

        public static InstructionPointer Nil = new(InstructionSequence.Empty, 0);

        public InstructionPointer Previous => new(Sequence, Index - 1);

        public bool Equals(InstructionPointer other)
        {
            return ReferenceEquals(Sequence, other.Sequence) && Index == other.Index;
        }

        public override bool Equals(object? obj)
        {
            return obj is InstructionPointer other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Sequence, Index);
        }
    }
}