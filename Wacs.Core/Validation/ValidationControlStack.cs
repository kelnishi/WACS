using System.Collections.Generic;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public class ValidationControlStack
    {
        private readonly Stack<Frame> _stack = new();

        public void PushFrame(Frame frame) => _stack.Push(frame);

        public void PopFrame() => _stack.Pop();

        public Frame Peek() => _stack.Peek();

    }
}