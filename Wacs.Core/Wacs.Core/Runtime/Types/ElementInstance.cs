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

using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.10. Element Instances
    /// </summary>
    public class ElementInstance
    {
        public readonly static ElementInstance Empty = new(ValType.FuncRef, new List<Value>());

        public ElementInstance(ValType type, List<Value> refs)
        {
            Type = type;
            //If the initialized value type fits, use it, otherwise coerce it to the proper type.
            //TODO fix this type resolution
            Elements = refs.Select(v => /*!v.Type.Matches(Type) ? new Value(v, Type) :*/ v).ToList();
        }

        public ValType Type { get; }

        //Refs
        public List<Value> Elements { get; }
    }
}