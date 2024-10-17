using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 3.1.1. Contexts
    /// </summary>
    public class WasmValidationContext
    {
        public ValidationOpStack OpStack { get; private set; } = new();
        public ValidationControlStack ControlStack { get; private set; } = new();

        public Stack<ResultType> Labels { get; private set; } = new();
        
        private ModuleInstance ValidationModule { get; set; }

        public TypesSpace Types => ValidationModule.Types;
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals => ControlStack.Peek().Locals;
        public ElementsSpace Elements { get; private set; }
        
        public DataValidationSpace Datas { get; private set; }
        
        
        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// @Spec 3.4.10. Modules
        /// </summary>
        public WasmValidationContext(Module module)
        {
            ValidationModule = new ModuleInstance(module);
            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = new ElementsSpace(module.Elements.ToList());
            Datas = new DataValidationSpace(module.Datas.Length);

            Globals = new GlobalValidationSpace(module);
            
        }
        
        public delegate string MessageProducer();

        public void Assert(bool factIsTrue, MessageProducer message)
        {
            if (!factIsTrue)
                throw new InvalidDataException(message());
        }

        

        public void PushFrame(Module.Function func)
        {
            var funcType = Types[func.TypeIndex];
            var locals = new LocalsSpace(funcType.ParameterTypes.Types, func.Locals);
            var frame = new Frame(ValidationModule)
            {
                Locals = locals,
                Return = funcType.ResultType
            };
            ControlStack.PushFrame(frame);
        }

        public void PopFrame() => ControlStack.PopFrame();
        
    }
    
}