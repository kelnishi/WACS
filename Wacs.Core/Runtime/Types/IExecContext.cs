using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    public delegate void ExecContextDelegate(WasmValidationContext context);
    
    public interface IExecContext
    {
        IOperandStack OpStack { get; }
        
        Stack<Frame> CallStack { get; }
        

        Stack<ResultType> Labels { get; }
        ResultType? Return { get; }
        
        //Don't need refs, we can interrogate FunctionsSpace for valid indices
        // HashSet<FuncIdx> Refs { get; }

        void PushFrame(Frame frame);
        
        LocalsSpace Locals { get; }
        GlobalsSpace Globals { get; }

        void ValidateContext(ExecContextDelegate del);

    }
}