using System.Collections.Generic;
using Wacs.Core.Runtime;

namespace Wacs.Core.Validation
{
    public class ValidationControlStack
    {
        private readonly Stack<Frame> _stack = new();

        public Frame Frame => _stack.Peek();

        public void PushFrame(Frame frame) => _stack.Push(frame);

        public void PopFrame() => _stack.Pop();
    }
}