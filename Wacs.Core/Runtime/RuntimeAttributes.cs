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

using Wacs.Core.Instructions;

namespace Wacs.Core.Runtime
{
    public class RuntimeAttributes
    {
        public int GrowCallStack = 512;

        public int GrowLabelsStack = 512;

        public int InitialCallStack = 512;
        public int InitialLabelsStack = 2048;
        public bool Live = true;
        public int LocalPoolSize = 64;
        public int MaxCallStack = 2048;

        public int MaxFunctionLocals = 2048;

        public int MaxOpStack = 256;
        public InstructionBaseFactory InstructionFactory { get; set; } = SpecFactory.Factory;
    }

}