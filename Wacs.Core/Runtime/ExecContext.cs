using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class ExecContext
    {
        public Store Store { get; }
        public OpStack OpStack { get; private set; } = new();
        public Stack<Frame> CallStack { get; private set; } = new();
        
        public Stack<ResultType> Labels { get; private set; } = new();
        public ResultType? Return { get; private set; } = null;
        public ExecContext(Store store)
        {
            Store = store;
        }

        public delegate string MessageProducer();
        
        public void Assert(bool factIsTrue, MessageProducer message)
        {
            if (!factIsTrue)
                throw new TrapException(message());
        }

        public void PushFrame(Frame frame)
        {
            CallStack.Push(frame);
        }

        public void PopFrame()
        {
            CallStack.Pop();
        }

        public Frame Frame => CallStack.Peek();
        
        public void SetLabels(IEnumerable<ResultType> labels) =>
            Labels = new Stack<ResultType>(labels);

        public void SetReturn(ResultType? type) =>
            Return = type;

    }
    
}