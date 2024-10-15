using System;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class FunctionRef
    {
        
    }

    public class ExecContext
    {
        public IOperandStack OpStack { get; private set; } = null!;
        
        public LabelStack LabelStack { get; set; }
        
        public FrameStack CallStack { get; set; }
            
        public Store Store { get; private set; } = null!;

        
        public TypesSpace Types { get; private set; } = null!;
        
        public FunctionsSpace Funcs { get; private set; } = null!;
        
        public TablesSpace Tables { get; private set; } = null!;
        
        public MemSpace Mems { get; private set; } = null!;
        
        public GlobalsSpace Globals { get; private set; } = null!;
        
        public List<Module.ElementSegment> Elements { get; private set; } = null!;
        
        // public List<Module.Data> Datas { get; private set; } = null!;

        // public List<FunctionRef> Refs { get; private set; } = null!;

        //TODO Move this into a frame or store?
        public List<Value> Locals { get; private set; } = new List<Value>();
        public Stack<ResultType> Labels { get; private set; } = new Stack<ResultType>();
        public ResultType? Return { get; private set; } = null;

        private bool Validates = false;

        public static ExecContext CreateExecContext(Module module) => new ExecContext {
            Types = new TypesSpace(module),
            
            Funcs = new FunctionsSpace(module),
            Tables = new TablesSpace(module),
            Mems = new MemSpace(module),
            Globals = new GlobalsSpace(module),
            
            Elements = module.Elements.ToList(),
            // Datas = module.Datas.ToList(),
            // Refs = BuildFunctionRefs(module),
            OpStack = new ExecStack(),
        };

        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// @Spec 3.4.10. Modules
        /// </summary>
        public static ExecContext CreateValidationContext(Module module) => new ExecContext {
            Types = new TypesSpace(module),
            
            Funcs = new FunctionsSpace(module),
            Tables = new TablesSpace(module),
            Mems = new MemSpace(module),
            Globals = new GlobalsSpace(module),
            
            Elements = module.Elements.ToList(),
            // Datas = module.Datas.ToList(),
            // Refs = BuildFunctionRefs(module),
            Validates = true,
            OpStack = new ValidationStack(),
        };
        
        public void SetLocals(params IEnumerable<ValType>[] types) =>
            Locals = types
                .SelectMany(collection => collection)
                .Select(t=> new Value(t))
                .ToList();

        public void SetLabels(IEnumerable<ResultType> labels) =>
            Labels = new Stack<ResultType>(labels);

        public void SetReturn(ResultType? type) =>
            Return = type;

        //TODO: resolve function Frame
        public Value GetLocal(LocalIdx index) =>
            Locals[(Index)index];
        //TODO: resolve function Frame
        public void SetLocal(LocalIdx index, Value value) =>
            Locals[(Index)index] = value;

        public Value GetGlobal(GlobalIdx index)
        {
            throw new NotImplementedException();
            // return Globals[index];
        }

        public void SetGlobal(GlobalIdx index, Value value)
        {
            throw new NotImplementedException();
            // Globals[index] = value;
        }
        
        public delegate void ExecContextDelegate(ExecContext context);
        
        public void ValidateContext(ExecContextDelegate del) {
            if (Validates)
                del(this);
        }

    }
    
}