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
using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class StoreArray : IGcRef
    {
        private readonly Value[] _data;

        private readonly ArrayType _definition;

        //Fill values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, Value elementVals, int n)
        {
            ArrayIndex = storeIndex;
            _definition = def;
            _data = new Value[n];
            Array.Fill(_data, elementVals);
            // for (int i = 0; i < n; ++i)
            // {
            //     _data[i] = elementVals;
            // }
        }

        //Default values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, int n)
        {
            ArrayIndex = storeIndex;
            _definition = def;
            _data = new Value[n];
            var fieldType = _definition.ElementType;
            var val = new Value(fieldType.UnpackType());
            Array.Fill(_data, val);
            // for (int i = 0; i < n; ++i)
            // {
            //     _data[i] = new Value(fieldType.UnpackType());
            // }
        }

        //Fixed values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, ref Stack<Value> values)
        {
            ArrayIndex = storeIndex;
            _definition = def;
            int n = values.Count;
            _data = new Value[n];
            var src = values.ToArray();
            Array.Copy(src, _data, n);
            // for (int i = 0; i < n; ++i)
            // {
            //     _data[i] = values.Pop();
            // }
        }

        //Data values
        public StoreArray(ArrayIdx storeIndex, ArrayType def, ReadOnlySpan<byte> data, int n, int stride)
        {
            ArrayIndex = storeIndex;
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
            ArrayIndex = storeIndex;
            _definition = def;
            int n = values.Count;
            _data = new Value[n];
            var src = values.ToArray();
            Array.Copy(src, _data, n);
            // for (int i = 0; i < n; ++i)
            // {
            //     _data[i] = values[i];
            // }
        }

        public ArrayIdx ArrayIndex { get; }

        public int Length => _data.Length;

        public Value this[int index]
        {
            get => _data[index];
            set => _data[index] = value;
        }

        public RefIdx StoreIndex => ArrayIndex;

        public void Fill(Value val, int offset, int count)
        {
            Array.Fill(_data, val,offset, count);
            // int end = offset + count;
            // for (int i = offset; i < end; ++i)
            // {
            //     _data[i] = val;
            // }
        }

        //Copy this array to a destination array
        public void Copy(int srcOffset, StoreArray dst, int dstOffset, int count)
        {
            Array.Copy(_data, srcOffset, dst._data, dstOffset, count);
            // int sStart = srcOffset;
            // int sEnd = sStart + count;
            // var sbuf = _data.AsSpan()[sStart..sEnd];
            //
            // int dStart = dstOffset;
            // int dEnd = dStart + count;
            // var dbuf = dst._data.AsSpan()[dStart..dEnd];
            // //For copying to/from the same array, go backwards if necessary to shift the data.
            // if (dStart < sStart)
            // {
            //     for (int i = 0; i < count; ++i)
            //     {
            //         dbuf[i] = sbuf[i];
            //     }    
            // }
            // else
            // {
            //     for (int i = count-1; i >= 0; --i)
            //     {
            //         dbuf[i] = sbuf[i];
            //     }
            // }
        }

        public void Init(int offset, ReadOnlySpan<byte> data, int n, int stride)
        {
            var ft = _definition.ElementType;
            int dstart = offset;
            int dend = dstart + n;
            var dest = _data.AsSpan()[dstart..dend];
            
            for (int i = 0; i < n; ++i)
            {
                int start = i * stride;
                int end = start + stride;
                var source = data[start..end];
                dest[i] = ft.UnpackValue(source);
            }
        }

        public void Init(int offset, List<Value> elems, int n)
        {
            var src = elems.ToArray();
            Array.Copy(src,  0, _data, offset, n);
        }
    }
}