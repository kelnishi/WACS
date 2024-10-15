using System.Collections.Generic;

namespace Wacs.Core.Runtime
{
    public class ValueStack
    {
        private readonly Stack<object> _stack = new Stack<object>();

        public void Push(object value)
        {
            _stack.Push(value);
        }

        public object Pop()
        {
            return _stack.Pop();
        }

        public T Pop<T>()
        {
            return (T)_stack.Pop();
        }

        public void PushI32(int value)
        {
            _stack.Push(value);
        }

        public int PopI32()
        {
            return (int)_stack.Pop();
        }

        public void PushI64(long value)
        {
            _stack.Push(value);
        }

        public long PopI64()
        {
            return (long)_stack.Pop();
        }

        public void PushF32(float value)
        {
            _stack.Push(value);
        }

        public float PopF32()
        {
            return (float)_stack.Pop();
        }

        public void PushF64(double value)
        {
            _stack.Push(value);
        }

        public double PopF64()
        {
            return (double)_stack.Pop();
        }
    }
}