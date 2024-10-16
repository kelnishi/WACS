using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class ValidationExecContext : IExecContext
    {
        public IOperandStack OpStack { get; private set; } = new ValidationStack();
        public Stack<Frame> CallStack { get; private set; } = new Stack<Frame>();
        public LocalsSpace Locals => CallStack.Peek().Locals;
        public GlobalsSpace Globals { get; set; }
        
        
        public Stack<ResultType> Labels { get; private set; } = new Stack<ResultType>();
        public ResultType? Return { get; private set; } = null;

        private WasmValidationContext Parent { get; }
        public ValidationExecContext(Module module, WasmValidationContext parent)
        {
            Parent = parent;
            Globals = new GlobalsSpace(module);
        }

        public void PushFrame(Frame frame)
        {
            CallStack.Push(frame);
        }
        
        public void SetLabels(IEnumerable<ResultType> labels) =>
            Labels = new Stack<ResultType>(labels);

        public void SetReturn(ResultType? type) =>
            Return = type;
        
        public void ValidateContext(ExecContextDelegate del) => del(Parent);
    }
    
    /// <summary>
    /// @Spec 3.1.1. Contexts
    /// </summary>
    public class WasmValidationContext
    {
        public ValidationExecContext ExecContext { get; }
        
        public TypesSpace Types { get; private set; } = null!;
        public FunctionsSpace Funcs { get; private set; } = null!;
        
        public TablesSpace Tables { get; private set; } = null!;
        
        public MemSpace Mems { get; private set; } = null!;
        
        public List<Module.ElementSegment> Elements { get; private set; } = null!;
        
        public List<bool> Datas { get; private set; } = null!;
        
        
        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// @Spec 3.4.10. Modules
        /// </summary>
        public WasmValidationContext(Module module)
        {
            Types = new TypesSpace(module);

            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = module.Elements.ToList();
            Datas = module.Datas.Select(data => true).ToList();

            ExecContext = new ValidationExecContext(module, this);
        }
    }
    
}