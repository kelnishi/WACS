using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class HostEnvironment
    {
        public Store Store { get; } = new Store();
        private Dictionary<string, ModuleInstance> modules = new Dictionary<string, ModuleInstance>();

        private Dictionary<(string module, string entity), IAddress> EntityBindings =
            new Dictionary<(string module, string entity), IAddress>(); 
        

        public void RegisterModule(string moduleName, ModuleInstance moduleInstance)
        {
            modules[moduleName] = moduleInstance;

            //Bind exports
            foreach (var export in moduleInstance.Exports)
            {
                EntityBindings[(moduleName, export.Name)] = export.Value switch {
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
            if (modules.TryGetValue(moduleName, out var moduleInstance))
            {
                return moduleInstance;
            }
            throw new Exception($"Module '{moduleName}' not found.");
        }

        public void BindHostFunction(string moduleName, string entityName)
        {
            
        }

        private IAddress? GetBoundEntity((string module, string entity) id) =>
            EntityBindings.TryGetValue(id, out IAddress value) ? value : null;
        

        public ModuleInstance InstantiateModule(Module module)
        {
            var moduleInstance = new ModuleInstance();
            
            //Resolve Imports
            foreach (var import in module.Imports)
            {
                var entityId = (module: import.ModuleName, entity: import.Name);
                switch (import.Desc)
                {
                    case Module.ImportDesc.FuncDesc funcDesc:
                        var funcSig = module[funcDesc.TypeIndex];
                        var funcAddr = GetBoundEntity(entityId) as FuncAddr;
                        if (funcAddr == null)
                            throw new NotSupportedException(
                                $"The imported Function was not provided by the environment: {entityId.module}.{entityId.entity} {funcSig.ToNotation()}");
                        var functionInstance = Store[funcAddr];
                        if (functionInstance.Type != funcSig)
                            throw new NotSupportedException(
                                $"Type mismatch while importing Function {entityId.module}.{entityId.entity}: expected {funcSig.ToNotation()}, env provided Function {functionInstance.Type.ToNotation()}");
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
                        moduleInstance.GlobalAddrs.Add(globalAddr);
                        break;
                }
            }

            //Instantiate Functions
            moduleInstance.FuncAddrs
                .AddRange(module.Funcs
                    .Select(func => new FunctionInstance(module[func.TypeIndex], func, moduleInstance))
                    .Select(functionInstance => Store.AddFunction(functionInstance)));
            
            //Instantiate Tables
            moduleInstance.TableAddrs
                .AddRange(module.Tables
                    .Select(tableType => new TableInstance(tableType))
                    .Select(tableInstance => Store.AddTable(tableInstance)));
            
            //Instantiate Memories
            moduleInstance.MemAddrs
                .AddRange( module.Memories
                    .Select(memType => new MemoryInstance(memType))
                    .Select(memInstance => Store.AddMemory(memInstance)));
            
            //Instantiate Globals
            moduleInstance.GlobalAddrs
                .AddRange( module.Globals
                    .Select(globalDef => new GlobalInstance(globalDef.Type, EvaluateInitializer(globalDef.Initializer)))
                    .Select(globalInstance => Store.AddGlobal(globalInstance)));
            
            //Process Exports
            foreach (var export in module.Exports)
            {
                var desc = export.Desc;
                ExternalValue val = desc switch {
                    Module.ExportDesc.FuncDesc funcDesc => 
                        new ExternalValue.Function(moduleInstance[funcDesc.FunctionIndex]),
                    Module.ExportDesc.TableDesc tableDesc => 
                        new ExternalValue.Table(moduleInstance[tableDesc.TableIndex]),
                    Module.ExportDesc.MemDesc memDesc => 
                        new ExternalValue.Memory(moduleInstance[memDesc.MemoryIndex]),
                    Module.ExportDesc.GlobalDesc globalDesc => 
                        new ExternalValue.Global(moduleInstance[globalDesc.GlobalIndex]),
                    _ => 
                        throw new InvalidDataException($"Invalid Export {desc}")
                };
                var exportInstance = new ExportInstance(export.Name, val);
                moduleInstance.Exports.Add(exportInstance);
            }

            //Set the start function, if it exists
            moduleInstance.StartFunction = module.StartIndex != FuncIdx.Default ? moduleInstance[module.StartIndex] : FuncAddr.Null;
            
            return moduleInstance;
        }

        private Value EvaluateInitializer(Expression ini)
        {
            throw new NotImplementedException();
            foreach (var inst in ini.Instructions)
            {
                //TODO
                // inst.Execute();
            }

            return Value.NullFuncRef;
        }
    }
}