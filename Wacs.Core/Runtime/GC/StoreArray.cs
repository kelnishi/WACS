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
using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class StoreArray : IGcRef
    {

        private ArrayType _definition;
        private Value[] _data;
        
        private ArrayIdx _index;
        public RefIdx StoreIndex => _index;
        public ArrayIdx ArrayIndex => _index;

        public int Length => _data.Length;

        public Value this[int index]
        {
            get => _data[index];
            set => _data[index] = value;
        }

        //Fill values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, Value elementVals, int n)
        {
            _index = storeIndex;
            _definition = def;

            _data = new Value[n];
            for (int i = 0; i < n; ++i)
            {
                _data[i] = elementVals;
            }
        }
        
        //Default values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, int n)
        {
            _index = storeIndex;
            _definition = def;
            _data = new Value[n];
            var fieldType = _definition.ElementType;
            for (int i = 0; i < n; ++i)
            {
                _data[i] = new Value(fieldType.UnpackType());
            }
        }
        
        //Fixed values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, ref Stack<Value> values)
        {
            _index = storeIndex;
            _definition = def;
            int n = values.Count;
            _data = new Value[n];
            for (int i = 0; i < n; ++i)
            {
                _data[i] = values.Pop();
            }
        }

        //Data values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, ReadOnlySpan<byte> data, int n, int stride)
        {
            _index = storeIndex;
            _definition = def;

            var ft = def.ElementType;
            _data = new Value[n];
            for (int i = 0; i < n; ++i)
            {
                int start = i * stride;
                int end = start + stride;
                var source = data[start..end];
                _data[i] = ft.UnpackValue(source);
            }
        }
        
        //Element values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, List<Value> values)
        {
            _index = storeIndex;
            _definition = def;
            int n = values.Count;
            _data = new Value[n];
            for (int i = 0; i < n; ++i)
            {
                _data[i] = values[i];
            }
        }
    }
}