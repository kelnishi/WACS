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
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.GC
{
    public class StoreStruct : IGcRef
    {
        private readonly Value[] _data;
        private readonly StructType _definition;

        public StoreStruct(StructIdx storeIndex, StructType def, Stack<Value> fieldVals)
        {
            StructIndex = storeIndex;
            _definition = def;

            _data = new Value[fieldVals.Count];
            for (int i = 0, l = fieldVals.Count; i < l; ++i)
            {
                _data[i] = fieldVals.Pop();
            }
        }

        //Defaults
        public StoreStruct(StructIdx storeIndex, StructType def)
        {
            StructIndex = storeIndex;
            _definition = def;
            var fieldTypes = _definition.FieldTypes;

            _data = new Value[fieldTypes.Length];
            for (int i = 0, l = fieldTypes.Length; i < l; ++i)
            {
                _data[i] = new Value(fieldTypes[i].UnpackType());
            }
        }

        public StructIdx StructIndex { get; }

        public Value this[FieldIdx y]
        {
            get => _data[y.Value];
            set => _data[y.Value] = value;
        }

        public RefIdx StoreIndex => StructIndex;
    }
}