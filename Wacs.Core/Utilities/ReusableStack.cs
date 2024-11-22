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
using System.Collections;
using System.Collections.Generic;

namespace Wacs.Core.Utilities
{
    public class ReusableStack<T>
        where T : IPoolable, new()
    {
        private readonly int _growSize;
        internal T[] _backingArray;

        internal int Top;

        public ReusableStack(int initialCapacity, int growSize = 0)
        {
            _growSize = growSize > 0 ? growSize : initialCapacity;
            _backingArray = new T[initialCapacity];

            for (int i = 0; i < initialCapacity; ++i)
            {
                _backingArray[i] = new T();
            }

            Top = -1;
        }

        internal T Peek()
        {
            if (Top < 0)
                throw new InvalidOperationException("Stack underflow");
            
            return _backingArray[Top];
        }

        internal T Pop()
        {
            if (Top < 0)
                throw new InvalidOperationException("Stack underflow");
            
            T top = _backingArray[Top];
            Top -= 1;
            return top;
        }

        internal T Reserve()
        {
            if (Top + 1 >= _backingArray.Length)
            {
                int i = _backingArray.Length;
                int l = i + _growSize;
                Array.Resize(ref _backingArray, l);
                for (; i < l; ++i)
                    _backingArray[i] = new T();
            }
            return _backingArray[Top+1];
        }

        internal void Push(T element)
        {
            Top += 1;
            _backingArray[Top] = element;
        }

        internal void Drop(int toIndex)
        {
            if (Top < toIndex)
                throw new InvalidOperationException("Stack underflow");
            
            Top = toIndex;
        }

        public SubStack<T> GetSubStack()
        {
            return new SubStack<T>(this);
        }
    }

    public struct SubStack<T> : IEnumerable<T> where T : IPoolable, new()
    {
        private readonly int _zeroElement;
        private readonly ReusableStack<T> _stack;

        public SubStack(ReusableStack<T> stack)
        {
            _stack = stack;
            _zeroElement = _stack.Top;
        }

        public T Peek() => _stack.Peek();

        public T Pop()
        {
            if (_stack.Top == _zeroElement)
                throw new InvalidOperationException("Stack underflow");
            
            return _stack.Pop();
        }

        public void Push(T element)
        {
            _stack.Push(element);
        }

        public int Count => _stack.Top - _zeroElement;

        public T Reserve()
        {
            var element = _stack.Reserve();
            //YOLO for perf
            // element.Clear();
            return element;
        }

        public void Drop() => _stack.Drop(_zeroElement);
        
        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator, IEnumerator<T>
        {
            private SubStack<T> _subStack;
            private int _currentIndex;

            public Enumerator(SubStack<T> subStack)
            {
                _subStack = subStack;
                _currentIndex = subStack.Count;
            }

            public void Reset()
            {
                _currentIndex = _subStack.Count;
            }

            object? IEnumerator.Current => Current;

            public T Current => _subStack._stack._backingArray[_subStack._zeroElement + _currentIndex];

            public bool MoveNext()
            {
                if (_currentIndex > 0)
                {
                    _currentIndex--;
                    return true;
                }
                return false;
            }

            public void Dispose()
            {
                
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        
    }
}