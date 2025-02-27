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

using System;
using System.Buffers;
using System.Collections.Generic;
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
        public ModuleInstance Module = null!;
        public Memory<Value> Locals;
        public Label ReturnLabel = new();
        public int Head;
        
        public void Clear()
        {
            Module = default!;
            Locals = default;
        }
    }
}