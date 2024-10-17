using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Runtime
    {
        public Store Store { get; }

        public ExecContext Context { get; }
        
        private readonly Dictionary<string, ModuleInstance> _modules = new();

        private readonly Dictionary<(string module, string entity), IAddress> _entityBindings = new();

        public Runtime()
        {
            Store = new Store();
            Context = new ExecContext(Store);
        }

        public void RegisterModule(string moduleName, ModuleInstance moduleInstance)
        {
            _modules[moduleName] = moduleInstance;

            //Bind exports
            foreach (var export in moduleInstance.Exports)
            {
                _entityBindings[(moduleName, export.Name)] = export.Value switch {
                    ExternalValue.Function func => func.Address,
                    ExternalValue.Table table => table.Address,
                    ExternalValue.Memory mem => mem.Address,
                    ExternalValue.Global global => global.Address,
                    _ => throw new InvalidDataException($"Corrupted Export Instance in {moduleName} ({export.Name})"),
                };
            }
        }

        public ModuleInstance GetModule(string moduleName)
        {
            if (_modules.TryGetValue(moduleName, out var moduleInstance))
            {
                return moduleInstance;
            }
            throw new Exception($"Module '{moduleName}' not found.");
        }

        private IAddress? GetBoundEntity((string module, string entity) id) =>
            _entityBindings.GetValueOrDefault(id);
        
        public void BindHostFunction(string moduleName, string entityName)
        {
            //TODO
        }

        /// <summary>
        /// @Spec 4.5.4. Instantiation
        /// </summary>
        public ModuleInstance InstantiateModule(Module module, RuntimeOptions options)
        {
            try
            {
                //1
                if (!options.SkipModuleValidation)
                    module.ValidateAndThrow();
            }
            catch (ValidationException exc)
            {
                _ = exc;
                throw;
            }

            ModuleInstance moduleInstance;
            try
            {
                //2, 3, 4 Checks if imports are satisfied
                moduleInstance = AllocateModule(module);
            }
            catch (NotSupportedException exc)
            {
                _ = exc;
                throw;
            }
            
            //12.
            var auxFrame = new Frame(moduleInstance) { Locals = new LocalsSpace() };
            //13.
            Context.PushFrame(auxFrame);
            
            //14, 15
            for (int i = 0, l = module.Elements.Length; i < l; ++i)
            {
                var elem = module.Elements[i];
                switch (elem.Mode)
                {
                    case Module.ElementMode.ActiveMode activeMode:
                        var n = elem.Initializers.Length;
                        activeMode.Offset
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const)!.Immediate(0)
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const)!.Immediate(n)
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstTableInit>(OpCode.TableInit)!.Immediate(activeMode.TableIndex, (ElemIdx)i)
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstElemDrop>(OpCode.ElemDrop)!.Immediate((ElemIdx)i)
                            .Execute(Context);
                        break;
                    case Module.ElementMode.DeclarativeMode declarativeMode:
                        InstructionFactory.CreateInstruction<InstElemDrop>(OpCode.ElemDrop)!.Immediate((ElemIdx)i)
                            .Execute(Context);
                        break;
                }
            }
            //16.
            for (int i = 0, l = module.Datas.Length; i < l; ++i)
            {
                var data = module.Datas[i];
                switch (data.Mode)
                {
                    case Module.DataMode.ActiveMode activeMode:
                        if (activeMode.MemoryIndex != 0)
                            throw new InvalidProgramException("Module could not be instantiated: Multiple Memories are not supported.");
                        var n = data.Init.Length;
                        activeMode.Offset
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const)!.Immediate(0)
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const)!.Immediate(n)
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstMemoryInit>(OpCode.MemoryInit)!.Immediate((DataIdx)i)
                            .Execute(Context);
                        InstructionFactory.CreateInstruction<InstDataDrop>(OpCode.DataDrop)!.Immediate((DataIdx)i)
                            .Execute(Context);
                        break;
                    case Module.DataMode.PassiveMode: //Do nothing
                        break;
                }
            }
            //17. 
            if (module.StartIndex != FuncIdx.Default)
            {
                if (!options.SkipStartFunction)
                {
                    if (!moduleInstance.FuncAddrs.Contains(module.StartIndex))
                        throw new InvalidDataException("Module StartFunction index was invalid");
                    var startAddr = moduleInstance.FuncAddrs[module.StartIndex];
                    if (!Context.Store.Contains(startAddr))
                        throw new InvalidProgramException("Module StartFunction address not found in the Store.");
                    //Invoke the function!
                    InstructionFactory.CreateInstruction<InstCall>(OpCode.Call)!.Immediate(module.StartIndex)
                        .Execute(Context);
                }
            }
            //18.
            if (Context.Frame != auxFrame)
                throw new InvalidProgramException("Execution fault in Module Instantiation.");
            //19.
            Context.PopFrame();
            
            return moduleInstance;
        }

        /// <summary>
        /// @Spec 4.5.3.10. Modules Allocation
        /// *We also evaluate globals and elements here, per instantiation
        /// </summary>
        private ModuleInstance AllocateModule(Module module)
        {
            //20. Include Types from module
            var moduleInstance = new ModuleInstance(module);
            
            //Resolve Imports
            foreach (var import in module.Imports)
            {
                var entityId = (module: import.ModuleName, entity: import.Name);
                switch (import.Desc)
                {
                    case Module.ImportDesc.FuncDesc funcDesc:
                        // @Spec 4.5.3.2. @note: Host Functions must be bound to the environment prior to module instantiation!
                        var funcSig = moduleInstance.Types[funcDesc.TypeIndex];
                        var funcAddr = GetBoundEntity(entityId) as FuncAddr;
                        if (funcAddr == null)
                            throw new NotSupportedException(
                                $"The imported Function was not provided by the environment: {entityId.module}.{entityId.entity} {funcSig.ToNotation()}");
                        var functionInstance = Store[funcAddr];
                        if (functionInstance.Type != funcSig)
                            throw new NotSupportedException(
                                $"Type mismatch while importing Function {entityId.module}.{entityId.entity}: expected {funcSig.ToNotation()}, env provided Function {functionInstance.Type.ToNotation()}");
                        //14. external imported addresses first
                        moduleInstance.FuncAddrs.Add(funcAddr);
                        break;
                    case Module.ImportDesc.TableDesc tableDesc:
                        var tableType = tableDesc.TableDef;
                        var tableAddr = GetBoundEntity(entityId) as TableAddr;
                        if (tableAddr == null)
                            throw new NotSupportedException(
                                $"The imported Table was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var tableInstance = Store[tableAddr];
                        if (tableInstance.Type != tableType)
                            throw new NotSupportedException(
                                $"Type mismatch while importing Table {entityId.module}.{entityId.entity}: expected {tableType}, env provided Table {tableInstance.Type}");
                        //15. external imported addresses first
                        moduleInstance.TableAddrs.Add(tableAddr);
                        break;
                    case Module.ImportDesc.MemDesc memDesc:
                        var memType = memDesc.MemDef;
                        var memAddr = GetBoundEntity(entityId) as MemAddr;
                        if (memAddr == null)
                            throw new NotSupportedException(
                                $"The imported Memory was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var memInstance = Store[memAddr];
                        if (memInstance.Type != memType)
                            throw new NotSupportedException(
                                $"Type mismatch while importing Memory {entityId.module}.{entityId.entity}: expected {memType}, env provided Memory {memInstance.Type}");
                        //16. external imported addresses first
                        moduleInstance.MemAddrs.Add(memAddr);
                        break;
                    case Module.ImportDesc.GlobalDesc globalDesc:
                        var globalType = globalDesc.GlobalDef;
                        var globalAddr = GetBoundEntity(entityId) as GlobalAddr;
                        if (globalAddr == null)
                            throw new NotSupportedException(
                                $"The imported Global was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var globalInstance = Store[globalAddr];
                        if (globalInstance.Type != globalType)
                            throw new NotSupportedException(
                                $"Type mismatch while importing table {entityId.module}.{entityId.entity}: expected {globalType}, env provided table {globalInstance.Type}");
                        //17. external imported addresses first
                        moduleInstance.GlobalAddrs.Add(globalAddr);
                        break;
                }
            }

            //2. Allocate Functions and capture their addresses in the Store
            //8. index ordered function addresses
            foreach (var func in module.Funcs) {
                moduleInstance.FuncAddrs.Add(AllocateFunc(Store, func, moduleInstance));
            }
            
            //3. Allocate Tables and capture their addresses in the Store
            //9. index ordered table addresses
            foreach (var table in module.Tables) {
                moduleInstance.TableAddrs.Add(AllocateTable(Store, table, Value.RefNull(table.ElementType)));
            }

            //4. Allocate Memories and capture their addresses in the Store
            //10. index ordered memory addresses
            foreach (var mem in module.Memories) {
                moduleInstance.MemAddrs.Add(AllocateMemory(Store, mem));
            }
            
            // @Spec 4.5.4 Step 7
            var initFrame = new Frame(moduleInstance) { Locals = new LocalsSpace() };
            Context.PushFrame(initFrame);
            
            //5. Allocate Globals and capture their addresses in the Store
            //11. index ordered global addresses
            foreach (var global in module.Globals) {
                var val = EvaluateExpression(global.Initializer);
                if (Context.Frame != initFrame)
                    throw new InvalidProgramException($"Call stack was manipulated while initializing globals");
                moduleInstance.GlobalAddrs.Add(AllocateGlobal(Store, global.Type, val));
            }
            
            //6. Allocate Elements
            //12. index ordered element addresses
            foreach (var elem in module.Elements) {
                var refs = EvaluateInitializers(elem.Initializers);
                moduleInstance.ElemAddrs.Add(AllocateElement(Store, elem.Type, refs));
            }
            
            // @Spec 4.5.4 Step 10
            Context.PopFrame();
            
            //7. Allocate Datas
            //13. index ordered data addresses
            foreach (var data in module.Datas) {
                moduleInstance.DataAddrs.Add(AllocateData(Store, data.Init));
            }
            
            //18. Collect exports
            //Process Exports, keep them, so they can be bound with a module name and imported by later modules.
            foreach (var export in module.Exports)
            {
                var desc = export.Desc;
                ExternalValue val = desc switch {
                    Module.ExportDesc.FuncDesc funcDesc => 
                        new ExternalValue.Function(moduleInstance.FuncAddrs[funcDesc.FunctionIndex]),
                    Module.ExportDesc.TableDesc tableDesc => 
                        new ExternalValue.Table(moduleInstance.TableAddrs[tableDesc.TableIndex]),
                    Module.ExportDesc.MemDesc memDesc => 
                        new ExternalValue.Memory(moduleInstance.MemAddrs[memDesc.MemoryIndex]),
                    Module.ExportDesc.GlobalDesc globalDesc => 
                        new ExternalValue.Global(moduleInstance.GlobalAddrs[globalDesc.GlobalIndex]),
                    _ => 
                        throw new InvalidDataException($"Invalid Export {desc}")
                };
                var exportInstance = new ExportInstance(export.Name, val);
                //19. indexed export addresses
                moduleInstance.Exports.Add(exportInstance);
            }
            return moduleInstance;
        }

        /// <summary>
        /// @Spec 4.5.3.1. Functions
        /// </summary>
        private static FuncAddr AllocateFunc(Store store, Module.Function func, ModuleInstance moduleInst)
        {
            var funcType = moduleInst.Types[func.TypeIndex];
            var funcInst = new FunctionInstance(funcType, moduleInst, func);
            var funcAddr = store.AddFunction(funcInst);
            return funcAddr;
        }
        
        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// </summary>
        private static FuncAddr AllocateHostFunc(Store store, FunctionType funcType, HostFunction.HostFunctionDelegate hostFunc)
        {
            var funcInst = new HostFunction(funcType, hostFunc);
            var funcAddr = store.AddFunction(funcInst);
            return funcAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.3. Tables
        /// </summary>
        private static TableAddr AllocateTable(Store store, TableType tableType, Value refVal)
        {
            var tableInst = new TableInstance(tableType, refVal);
            var tableAddr = store.AddTable(tableInst);
            return tableAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.4. Memories
        /// </summary>
        private static MemAddr AllocateMemory(Store store, MemoryType memType)
        {
            var memInst = new MemoryInstance(memType);
            var memAddr = store.AddMemory(memInst);
            return memAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.5. Globals
        /// </summary>
        private static GlobalAddr AllocateGlobal(Store store, GlobalType globalType, Value val)
        {
            var globalInst = new GlobalInstance(globalType, val);
            var globalAddr = store.AddGlobal(globalInst);
            return globalAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.6. Element segments
        /// </summary>
        private static ElemAddr AllocateElement(Store store, ReferenceType refType, List<Value> refs)
        {
            var elemInst = new ElementInstance(refType, refs);
            var elemAddr = store.AddElement(elemInst);
            return elemAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.7. Data Segments
        /// </summary>
        private static DataAddr AllocateData(Store store, byte[] init)
        {
            var dataInst = new DataInstance(init);
            var dataAddr = store.AddData(dataInst);
            return dataAddr;
        }
        
        /// <summary>
        /// @Spec 4.5.4. Instantiation
        /// Step 8.1
        /// </summary>
        private Value EvaluateExpression(Expression ini)
        {
            ini.Execute(Context);
            var value = Context.OpStack.PopAny();
            return value;
        }

        private List<Value> EvaluateInitializers(Expression[] inis)
        {
            return inis.Select(ini => EvaluateExpression(ini)).ToList();
        }
    }
}