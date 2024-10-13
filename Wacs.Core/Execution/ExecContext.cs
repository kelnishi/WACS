using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Types;
using Wacs.Core.Runtime;

namespace Wacs.Core.Execution
{
    public class FunctionRef
    {
        
    }

    public class ExecContext
    {
        public IOperandStack Stack { get; private set; } = null!;

        public List<FunctionType> Types { get; private set; } = null!;
        
        public List<Module.Function> Funcs { get; private set; } = null!;
        
        public List<TableType> Tables { get; private set; } = null!;
        
        public List<MemoryType> Mems { get; private set; } = null!;
        
        public List<GlobalType> Globals { get; private set; } = null!;
        
        public List<Module.ElementSegment> Elements { get; private set; } = null!;
        
        public List<Module.Data> Datas { get; private set; } = null!;

        public List<FunctionRef> Refs { get; private set; } = null!;

        public List<StackValue> Locals { get; private set; } = new List<StackValue>();
        public Stack<ResultType> Labels { get; private set; } = new Stack<ResultType>();
        public ResultType? Return { get; private set; } = null;

        private bool Validates = false;

        public static ExecContext CreateExecContext(Module module) => new ExecContext {
            Stack = new ExecStack(),
            Types = module.Types.ToList(),
            Funcs = module.Funcs,
            Tables = module.Tables.ToList(),
            Mems = module.Mems,
            Globals = module.Globals.Select(g => g.Type).ToList(),
            Elements = module.Elements.ToList(),
            Datas = module.Datas.ToList(),
            Refs = BuildFunctionRefs(module),
        };

        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// </summary>
        public static ExecContext CreateValidationContext(Module module) => new ExecContext {
            Stack = new ValidationStack(),
            Types = module.Types.ToList(),
            Funcs = module.Funcs,
            Tables = module.Tables.ToList(),
            Mems = module.Mems,
            Globals = module.Globals.Select(g => g.Type).ToList(),
            Elements = module.Elements.ToList(),
            Datas = module.Datas.ToList(),
            Refs = BuildFunctionRefs(module),
            Validates = true,
        };

        private static List<FunctionRef> BuildFunctionRefs(Module module)
        {
            var result = new List<FunctionRef>();

            foreach (var import in module.Imports)
            {
                if (import.Desc is Module.ImportDesc.FuncDesc fdesc)
                {
                    result.Add(new FunctionRef());
                }
            }

            foreach (var func in module.Funcs)
            {
                result.Add(new FunctionRef());
            }

            // foreach (var table in module.Tables)
            // {
            //     
            // }

            return result;
        }
        
        public void SetLocals(params IEnumerable<ValType>[] types) =>
            Locals = types
                .SelectMany(collection => collection)
                .Select(t=> new StackValue(t))
                .ToList();

        public void SetLabels(IEnumerable<ResultType> labels) =>
            Labels = new Stack<ResultType>(labels);

        public void SetReturn(ResultType? type) =>
            Return = type;

        //TODO: resolve function Frame
        public StackValue GetLocal(uint index) =>
            Locals[(int)index];
        //TODO: resolve function Frame
        public void SetLocal(uint index, StackValue value) =>
            Locals[(int)index] = value;

        public StackValue GetGlobal(uint index) => StackValue.NullFuncRef;
            // Globals[(int)index];

        public void SetGlobal(uint index, StackValue value) {}
            // Globals[(int)index] = value;
        
        public delegate void ExecContextDelegate(ExecContext context);
        
        public void ValidateContext(ExecContextDelegate del) {
            if (Validates)
                del(this);
        }

    }
    
}