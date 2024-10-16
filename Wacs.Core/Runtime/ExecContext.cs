using System;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class ExecContext : IExecContext
    {
        public IOperandStack OpStack { get; private set; } = new ValidationStack();
        public Stack<Frame> CallStack { get; private set; } = new Stack<Frame>();
        public LocalsSpace Locals => CallStack.Peek().Locals;
        public GlobalsSpace Globals { get; set; }
        
        
        public Stack<ResultType> Labels { get; private set; } = new Stack<ResultType>();
        public ResultType? Return { get; private set; } = null;

        public ExecContext()
        {
            //TODO Specialize this class
            Globals = new GlobalsSpace(null);
        }

        public void PushFrame(Frame frame)
        {
            CallStack.Push(frame);
        }
        
        public void SetLabels(IEnumerable<ResultType> labels) =>
            Labels = new Stack<ResultType>(labels);

        public void SetReturn(ResultType? type) =>
            Return = type;
        
        public void ValidateContext(ExecContextDelegate del) {}
    }
    
}