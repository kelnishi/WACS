// Copyright 2025 Kelvin Nishikawa
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

using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    public class ExnInstance : IGcRef
    {
        public Stack<Value> Fields;
        public ExnIdx Index;

        public TagAddr Tag;

        public ExnInstance(uint idx, TagAddr tag, Stack<Value> fields)
        {
            Index = new ExnIdx(idx);
            Tag = tag;
            Fields = fields;
        }

        public RefIdx StoreIndex => Index;
    }
}