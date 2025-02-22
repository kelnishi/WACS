// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

// using System.Diagnostics.CodeAnalysis;

namespace Wacs.Core.Runtime
{
    public partial class WasmRuntime
    {
        private readonly Dictionary<(string module, string entity), IAddress?> _entityBindings = new();

        private readonly List<ModuleInstance> _moduleInstances = new();
        private readonly Dictionary<string, ModuleInstance> _registeredModules = new();

        private readonly ExecContext Context;

        private readonly Store Store;

        //Cached instructions for module initialization
        private readonly InstElemDrop _dropInst;
        private readonly InstI32Const _i32ConstInst;
        private readonly InstTableInit _tableInitInst;
        private readonly InstMemoryInit _memoryInitInst;
        private readonly InstDataDrop _dataDropInst;

        public WasmRuntime(RuntimeAttributes? attributes = null)
        {
            Store = new Store();
            Context = new ExecContext(Store, attributes);
            
            //Cached instructions for module initialization
            _dropInst = SpecFactory.Factory.CreateInstruction<InstElemDrop>(ExtCode.ElemDrop);
            _i32ConstInst = SpecFactory.Factory.CreateInstruction<InstI32Const>(OpCode.I32Const);
            _tableInitInst = SpecFactory.Factory.CreateInstruction<InstTableInit>(ExtCode.TableInit);
            _memoryInitInst = SpecFactory.Factory.CreateInstruction<InstMemoryInit>(ExtCode.MemoryInit);
            _dataDropInst = SpecFactory.Factory.CreateInstruction<InstDataDrop>(ExtCode.DataDrop);
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
                        var type = moduleInstance.Types[funcDesc.TypeIndex];
                        var funcSig = type.Expansion as FunctionType;
                        if (funcSig is null)
                            throw new InvalidDataException($"Function had invalid type:{type}");
                        if (GetBoundEntity(entityId) is not FuncAddr funcAddr)
                            throw new NotSupportedException(
                                $"The imported Function was not provided by the environment: {entityId.module}.{entityId.entity} {funcSig.ToNotation()}");
                        var functionInstance = Store[funcAddr];
                        if (functionInstance is FunctionInstance wasmFunc)
                        {
                            wasmFunc.SetName(entityId.entity);
                            if (!wasmFunc.DefType.Matches(type, moduleInstance.Types))
                                throw new NotSupportedException(
                                    $"Recursive Type mismatch while importing Function {entityId.module}.{entityId.entity}: expected {funcSig.ToNotation()}, env provided Function {functionInstance.Type.ToNotation()}");    
                        }
                        if (!functionInstance.Type.Matches(funcSig, moduleInstance.Types))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Function {entityId.module}.{entityId.entity}: expected {funcSig.ToNotation()}, env provided Function {functionInstance.Type.ToNotation()}");
                        //14. external imported addresses first
                        moduleInstance.FuncAddrs.Add(funcAddr);
                        break;
                    case Module.ImportDesc.TableDesc tableDesc:
                        var tableType = tableDesc.TableDef;
                        if (GetBoundEntity(entityId) is not TableAddr tableAddr)
                            throw new NotSupportedException(
                                $"The imported Table was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var tableInstance = Store[tableAddr];
                        if (!tableType.IsCompatibleWith(tableInstance.Type))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Table {entityId.module}.{entityId.entity}: expected {tableType}, env provided Table {tableInstance.Type}");
                        //15. external imported addresses first
                        moduleInstance.TableAddrs.Add(tableAddr);
                        break;
                    case Module.ImportDesc.MemDesc memDesc:
                        var memType = memDesc.MemDef;
                        if (GetBoundEntity(entityId) is not MemAddr memAddr)
                            throw new NotSupportedException(
                                $"The imported Memory was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var memInstance = Store[memAddr];
                        if (!memType.IsCompatibleWith(memInstance.Type))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Memory {entityId.module}.{entityId.entity}: expected {memType}, env provided Memory {memInstance.Type}");
                        //16. external imported addresses first
                        moduleInstance.MemAddrs.Add(memAddr);
                        break;
                    case Module.ImportDesc.GlobalDesc globalDesc:
                        var globalType = globalDesc.GlobalDef;
                        if (GetBoundEntity(entityId) is not GlobalAddr globalAddr)
                            throw new NotSupportedException(
                                $"The imported Global was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var globalInstance = Store[globalAddr];
                        if (globalType.Mutability != globalInstance.Type.Mutability)
                            throw new NotSupportedException(
                                $"Mutability mismatch while importing Global {entityId.module}.{entityId.entity} {globalType}, env provided Global {globalInstance.Type}");
                        if (globalInstance.Type.ContentType.IsDefType() &&
                            !moduleInstance.Types.Contains(globalInstance.Type.ContentType.Index()))
                            throw new NotSupportedException(
                                $"Incompatible import type for Global {entityId.module}.{entityId.entity}: {globalInstance.Type}");
                        if (!globalInstance.Type.Matches(globalType, moduleInstance.Types))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Global {entityId.module}.{entityId.entity}: expected {globalType}, env provided Global {globalInstance.Type}");
                        
                        //17. external imported addresses first
                        moduleInstance.GlobalAddrs.Add(globalAddr);
                        break;
                    case Module.ImportDesc.TagDesc tagDesc:
                        if (GetBoundEntity(entityId) is not TagAddr tagAddr)
                            throw new NotSupportedException(
                                $"The imported Tag was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var tagInstance = Store[tagAddr];
                        var importedType = tagInstance.Type;
                        var tagType = moduleInstance.Types[tagDesc.TagDef.TypeIndex];
                        if (!importedType.Matches(tagType, moduleInstance.Types))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Tag {entityId.module}.{entityId.entity}: expected {tagType.Expansion}, env provided Tag {tagInstance.Type}");
                        
                        moduleInstance.TagAddrs.Add(tagAddr);
                        break;
                }
            }

            
            //2. Allocate Functions and capture their addresses in the Store
            //8. index ordered function addresses
            foreach (var func in module.Funcs)
            {
                moduleInstance.FuncAddrs.Add(AllocateWasmFunc(Store, func, moduleInstance));
            }

            //@Spec 4.5.4 Step 7
            Frame initFrame = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.TableInitializers);
            Context.PushFrame(initFrame);
            
            //3. Allocate Tables and capture their addresses in the Store
            //9. index ordered table addresses
            foreach (var table in module.Tables)
            {
                var refVal = EvaluateInitializer(table.Init);
                if (Context.Frame != initFrame)
                    throw new TrapException($"Call stack was manipulated while initializing tables");
                moduleInstance.TableAddrs.Add(AllocateTable(Store, table, refVal));
            }

            //4. Allocate Memories and capture their addresses in the Store
            //10. index ordered memory addresses
            foreach (var mem in module.Memories)
            {
                moduleInstance.MemAddrs.Add(AllocateMemory(Store, mem));
            }
            //Make the address space permanent
            moduleInstance.MemAddrs.Finalize();

            Context.PopFrame();
            
            foreach (var tag in module.Tags)
            {
                if (tag.Attribute != TagTypeAttribute.Exception)
                    throw new InvalidDataException($"Tag must be an exception tag, found {tag.Attribute}");
                if (!moduleInstance.Types.Contains(tag.TypeIndex))
                    throw new InvalidDataException($"Tag type index {tag.TypeIndex} not found in types");
                var tagType = moduleInstance.Types[tag.TypeIndex];
                if (tagType.Expansion is not FunctionType)
                    throw new InvalidDataException($"Tag type must be a function type, found {tagType.Expansion}");

                moduleInstance.TagAddrs.Add(AllocateTag(Store, tagType));
            }
            
            Frame initFrame2 = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.GlobalInitializers);
            Context.PushFrame(initFrame2);

            //5. Allocate Globals and capture their addresses in the Store
            //11. index ordered global addresses
            foreach (var global in module.Globals)
            {
                var val = EvaluateInitializer(global.Initializer);
                if (Context.Frame != initFrame2)
                    throw new TrapException($"Call stack was manipulated while initializing globals");
                moduleInstance.GlobalAddrs.Add(AllocateGlobal(Store, global.Type, val));
            }

            Context.PopFrame();
            Frame initFrame3 = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.ElementInitializers);
            Context.PushFrame(initFrame3);

            //6. Allocate Elements
            //12. index ordered element addresses
            foreach (var elem in module.Elements)
            {
                var refs = EvaluateInitializers(elem.Initializers);
                moduleInstance.ElemAddrs.Add(AllocateElement(Store, elem.Type, refs));
            }

            // @Spec 4.5.4 Step 10
            Context.PopFrame();

            //7. Allocate Datas
            //13. index ordered data addresses
            foreach (var data in module.Datas)
            {
                moduleInstance.DataAddrs.Add(AllocateData(Store, data.Init));
            }

            //18. Collect exports
            //Process Exports, keep them, so they can be bound with a module name and imported by later modules.
            HashSet<string> exportedEntities = new();
            foreach (var export in module.Exports)
            {
                if (exportedEntities.Contains(export.Name))
                    throw new InvalidDataException($"Module had multiple exports named {export.Name}");
                exportedEntities.Add(export.Name);
                
                var desc = export.Desc;
                ExternalValue val = desc switch
                {
                    Module.ExportDesc.FuncDesc funcDesc =>
                        new ExternalValue.Function(moduleInstance.FuncAddrs[funcDesc.FunctionIndex]),
                    Module.ExportDesc.TableDesc tableDesc =>
                        new ExternalValue.Table(moduleInstance.TableAddrs[tableDesc.TableIndex]),
                    Module.ExportDesc.MemDesc memDesc =>
                        new ExternalValue.Memory(moduleInstance.MemAddrs[memDesc.MemoryIndex]),
                    Module.ExportDesc.GlobalDesc globalDesc =>
                        new ExternalValue.Global(moduleInstance.GlobalAddrs[globalDesc.GlobalIndex]),
                    Module.ExportDesc.TagDesc tagDesc =>
                        new ExternalValue.Tag(moduleInstance.TagAddrs[tagDesc.TagIndex]),
                    _ =>
                        throw new InvalidDataException($"Invalid Export {desc}")
                };
                var exportInstance = new ExportInstance(export.Name, val);
                //19. indexed export addresses
                moduleInstance.Exports.Add(exportInstance);
            }

            //Patch in export names
            var exportedFuncs = moduleInstance.Exports
                .Where(exp => exp.Value is ExternalValue.Function);
            foreach (var export in exportedFuncs)
            {
                var funcDesc = export.Value as ExternalValue.Function;
                var funcAddr = funcDesc!.Address;
                var funcInst = Store[funcAddr];
                if (!funcInst.IsExport)
                {
                    funcInst.SetName(export.Name);
                    funcInst.IsExport = true;
                }
            }

            return moduleInstance;
        }

        /// <summary>
        /// @Spec 4.5.4. Instantiation
        /// </summary>
        public ModuleInstance InstantiateModule(Module module, RuntimeOptions? options = default)
        {
            options ??= new RuntimeOptions();
            
            Stopwatch instantiationTimer = new();
            if (options.TimeInstantiation)
            {
                instantiationTimer.Reset();
                instantiationTimer.Start();
            }
            
            try
            {
                //1
                if (!options.SkipModuleValidation)
                    module.ValidateAndThrow();
            }
            catch (ValidationException exc)
            {
                ExceptionDispatchInfo.Throw(exc);
            }
            
            ModuleInstance moduleInstance = null!;
            try
            {
                Store.OpenTransaction();
                
                if (Context.OpStack.Count != 0)
                    throw new WasmRuntimeException("OpStack should be empty");

                //2, 3, 4 Checks if imports are satisfied
                moduleInstance = AllocateModule(module);

                //12.
                var auxFrame = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.ElementInitialization);
                //13.
                Context.PushFrame(auxFrame);
                try
                {
                    //14, 15
                    for (int i = 0, l = module.Elements.Length; i < l; ++i)
                    {
                        var elem = module.Elements[i];
                        switch (elem.Mode)
                        {
                            case Module.ElementMode.ActiveMode activeMode:
                                activeMode.Offset.ExecuteInitializer(Context);
                                _i32ConstInst.Immediate(0).Execute(Context);
                                _i32ConstInst.Immediate(elem.Initializers.Length).Execute(Context);
                                _tableInitInst.Immediate(activeMode.TableIndex, (ElemIdx)i).Execute(Context);
                                _dropInst.Immediate((ElemIdx)i).Execute(Context);
                                break;
                            case Module.ElementMode.DeclarativeMode declarativeMode:
                                _ = declarativeMode;
                                _dropInst.Immediate((ElemIdx)i).Execute(Context);
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
                                activeMode.Offset.ExecuteInitializer(Context);
                                _i32ConstInst.Immediate(0).Execute(Context);
                                _i32ConstInst.Immediate(data.Init.Length).Execute(Context);
                                _memoryInitInst.Immediate((DataIdx)i, activeMode.MemoryIndex).Execute(Context);
                                _dataDropInst.Immediate((DataIdx)i).Execute(Context);
                                break;
                            case Module.DataMode.PassiveMode: //Do nothing
                                break;
                        }
                    }
                }
                catch (TrapException exc)
                {
                    //Linking may succeed, so we commit the transaction
                    Store.CommitTransaction();
                    Store.OpenTransaction();
                    Context.FlushCallStack();
                    ExceptionDispatchInfo.Throw(exc);
                }
                finally
                {
                    if (TranspileModules)
                        TranspileModule(moduleInstance);

                    LinkModule(moduleInstance);

                    Context.CacheInstructions();
                }
                
                //17. 
                if (module.StartIndex != FuncIdx.Default)
                {
                    if (!moduleInstance.FuncAddrs.Contains(module.StartIndex))
                        throw new ValidationException("Module StartFunction index was invalid");
                    
                    var startAddr = moduleInstance.FuncAddrs[module.StartIndex];
                    if (!Context.Store.Contains(startAddr))
                        throw new WasmRuntimeException("Module StartFunction address not found in the Store.");

                    moduleInstance.StartFunc = startAddr;

                    //Invoke the function!
                    if (!options.SkipStartFunction)
                    {
                        try
                        {
                            var startInvoker = CreateStackInvoker(startAddr);
                            startInvoker(Array.Empty<Value>());
                        }
                        catch (TrapException)
                        {
                            //see linking.wast: line 412
                            // We're supposed to commit if the start function traps I guess...
                            Store.CommitTransaction();
                            Store.OpenTransaction();
                            throw;
                        }
                    }
                }

                //18.
                if (Context.Frame != auxFrame)
                    throw new InstantiationException("Execution fault in Module Instantiation.");
                //19.
                Context.PopFrame();

                _moduleInstances.Add(moduleInstance);
                
            }
            catch (WasmRuntimeException exc)
            {
                Store.DiscardTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                ExceptionDispatchInfo.Throw(exc);
            }
            catch (OutOfBoundsTableAccessException exc)
            {
                //The spec after v1 says to just keep active elements?
                // see linking.wast:264
                Store.CommitTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                ExceptionDispatchInfo.Throw(exc);
            }
            catch (TrapException exc)
            {
                //Linking may succeed, so we commit the transaction
                Store.CommitTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                ExceptionDispatchInfo.Throw(exc);
            }
            catch (NotSupportedException exc)
            {
                //Unlinkable
                Store.DiscardTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                ExceptionDispatchInfo.Throw(exc);
            }
            finally
            {
                Store.CommitTransaction();
            }

            if (options.TimeInstantiation)
            {
                instantiationTimer.Stop();
                Console.Error.WriteLine($"Instantiating module took {instantiationTimer.ElapsedMilliseconds:#0.###}ms");
            }
            
            return moduleInstance;
        }

        /// <summary>
        /// Serialize all function instructions into the context
        /// </summary>
        /// <param name="moduleInstance"></param>
        private void LinkModule(ModuleInstance moduleInstance)
        {
            foreach (var funcAddr in moduleInstance.FuncAddrs)
            {
                var instance = Store[funcAddr];
                if (instance is FunctionInstance functionInstance)
                {
                    if (functionInstance.Module != moduleInstance) 
                        continue;
                    Context.LinkFunction(functionInstance);
                }
            }
            
        }

        public void SynchronizeFunctionCalls(ModuleInstance moduleInstance)
        {
            foreach (var funcAddr in moduleInstance.FuncAddrs)
            {
                var instance = Store[funcAddr];
                if (instance is FunctionInstance { Definition: { IsImport: false } } functionInstance)
                    if (functionInstance.Module == moduleInstance)
                    {
                        var expr = functionInstance.Body;
                        SynchronizeInstructions(moduleInstance, expr.Instructions);
                    }
            }
        }

        private void SynchronizeInstructions(ModuleInstance moduleInstance, InstructionSequence seq)
        {
            foreach (var inst in seq)
            {
                switch (inst)
                {
                    case InstCall instCall:
                        var addr = moduleInstance.FuncAddrs[instCall.X];
                        var funcInst = Store[addr];
                        instCall.IsAsync = funcInst switch
                        {
                            FunctionInstance => false,
                            HostFunction hostFunction => hostFunction.IsAsync,
                            _ => instCall.IsAsync
                        };
                        break;
                    case InstBlock instBlock:
                        SynchronizeInstructions(moduleInstance, instBlock.GetBlock(0).Instructions);
                        break;
                    case InstLoop instLoop:
                        SynchronizeInstructions(moduleInstance, instLoop.GetBlock(0).Instructions);
                        break;
                    case InstIf instIf:
                        SynchronizeInstructions(moduleInstance, instIf.GetBlock(0).Instructions);
                        SynchronizeInstructions(moduleInstance, instIf.GetBlock(1).Instructions);
                        break;
                }
            }
        }

        /// <summary>
        /// @Spec 4.5.3.1. Functions
        /// </summary>
        private static FuncAddr AllocateWasmFunc(Store store, Module.Function func, ModuleInstance moduleInst)
        {
            return store.AllocateWasmFunction(func, moduleInst);
        }

        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// </summary>
        private static FuncAddr AllocateHostFunc(Store store, (string module, string entity) id, FunctionType funcType, Type delType, Delegate hostFunc, bool isAsync)
        {
            return store.AllocateHostFunction(id, funcType, delType, hostFunc, isAsync);
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
        
        private static TagAddr AllocateTag(Store store, DefType tagType)
        {
            var tagInst = new TagInstance(tagType);
            var tagAddr = store.AddTag(tagInst);
            return tagAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.6. Element segments
        /// </summary>
        private static ElemAddr AllocateElement(Store store, ValType refType, List<Value> refs)
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
        /// Execute instructions without gas
        /// </summary>
        private Value EvaluateInitializer(Expression ini)
        {
            if (ini.LabelTarget.Label.Arity != 1)
                throw new InvalidDataException("Initializers must have arity of 1");
            
            ini.ExecuteInitializer(Context);
            
            var value = Context.OpStack.PopAny();
            if (Context.OpStack.Count > 0)
                throw new WasmRuntimeException("Values left on stack");
            
            return value;
        }

        private List<Value> EvaluateInitializers(Expression[] inis)
        {
            return inis.Select(EvaluateInitializer).ToList();
        }
    }

   
}