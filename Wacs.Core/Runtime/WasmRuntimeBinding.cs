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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime
{
    public partial class WasmRuntime
    {
        public void RegisterModule(string moduleName, ModuleInstance moduleInstance)
        {
            _registeredModules[moduleName] = moduleInstance;

            //Bind exports
            foreach (var export in moduleInstance.Exports)
            {
                _entityBindings[(moduleName, export.Name)] = export.Value switch
                {
                    ExternalValue.Function func => func.Address,
                    ExternalValue.Table table => table.Address,
                    ExternalValue.Memory mem => mem.Address,
                    ExternalValue.Global global => global.Address,
                    ExternalValue.Tag tag => tag.Address,
                    _ => throw new InvalidDataException($"Corrupted Export Instance in {moduleName} ({export.Name})"),
                };
            }
        }

        public ModuleInstance GetModule(string? moduleName)
        {
            if (moduleName == null)
                return _moduleInstances.Last();
            
            if (_registeredModules.TryGetValue(moduleName, out var moduleInstance))
            {
                return moduleInstance;
            }
            
            var anonInstance = _moduleInstances.FirstOrDefault(m => m.Name == moduleName);
            if (anonInstance != null)
                return anonInstance;
            
            throw new Exception($"Module '{moduleName}' not found.");
        }

        public bool TryGetExportedFunction(string entity, out FuncAddr addr)
        {
            var exports = _moduleInstances.SelectMany(modInst => modInst.Exports)
                .Where(export => export.Name == entity)
                .Select(export => export.Value)
                .OfType<ExternalValue.Function>()
                .Select(func => func.Address);
            addr = exports.LastOrDefault();
            return addr != null;
        }

        public bool TryGetExportedFunction((string module, string entity) id, out FuncAddr addr)
        {
            try
            {
                addr = GetExportedFunction(id);
                return true;
            }
            catch (UnboundEntityException)
            {
                var exports = _moduleInstances
                    .Where(modInst => modInst.Name == id.module)
                    .SelectMany(modInst => modInst.Exports)
                    .Where(export => export.Name == id.entity)
                    .Select(export => export.Value)
                    .OfType<ExternalValue.Function>()
                    .Select(func => func.Address)
                    .ToList();

                if (exports.Count > 0)
                {
                    addr = exports.Last();
                    return true;
                }
                addr = FuncAddr.Null;
                return false;
            }
        }

        public FuncAddr GetExportedFunction((string module, string entity) id)
        {
            if (GetBoundEntity(id) is FuncAddr addr)
            {
                return addr;
            }

            throw new UnboundEntityException($"Function {id} was not exported from any modules currently loaded in the runtime.");
        }

        private IAddress? GetBoundEntity((string module, string entity) id) =>
            _entityBindings.GetValueOrDefault(id);

        public void BindHostFunction<TDelegate>((string module, string entity) id, TDelegate func)
            where TDelegate : Delegate
        {
            var funcType = func.GetType();
            var parameters = funcType.GetMethod("Invoke")?.GetParameters();
            var paramTypes = parameters?
                                 .Where(p=> !p.Attributes.HasFlag(ParameterAttributes.Out))
                                 .Select(p => p.ParameterType)
                                 .ToArray()
                             ?? Array.Empty<Type>();
            var outTypes = parameters?
                               .Where(p=> p.Attributes.HasFlag(ParameterAttributes.Out))
                               .Select(p => p.ParameterType)
                               .ToArray()
                           ?? Array.Empty<Type>();
            
            var paramValTypes = new ResultType(paramTypes);
            
            var returnTypeInfo = funcType.GetMethod("Invoke")?.ReturnType;
            ValType returnType = ValType.Nil;
            bool isAsync = false;
            if (returnTypeInfo is not null)
            {
                if (returnTypeInfo.BaseType == typeof(Task))
                {
                    isAsync = true;
                    if (returnTypeInfo.IsGenericType)
                    {
                        returnType = returnTypeInfo.GenericTypeArguments[0].ToValType();
                    }
                }
                else
                {
                    returnType = returnTypeInfo.ToValType();
                }
            }
            
            var outValTypes = outTypes.Select(t => ValTypeUtilities.UnpackRef(t)).ToArray();

            if (returnType != ValType.Nil)
            {
                outValTypes = new ValType[] { returnType }.Concat(outValTypes).ToArray();
            }
            var returnValType = new ResultType(outValTypes);
            
            for (int i = paramValTypes.Types.Length - 1; i >= 0; --i)
            {
                if (paramValTypes.Types[i] == ValType.ExecContext)
                {
                    if (i > 0)
                    {
                        throw new ArgumentException(
                            "ExecContext may only be the first parameter of a bound host function.");
                    }
                    //If it's the first, just unshift it.
                    paramValTypes = new ResultType(paramValTypes.Types.Skip(1).ToArray());
                }
            }

            Store.OpenTransaction();
            var type = new FunctionType(paramValTypes, returnValType);
            var funcAddr = AllocateHostFunc(Store, id, type, funcType, func, isAsync);
            Store.CommitTransaction();
            _entityBindings[id] = funcAddr;
        }

        public string GetFunctionName(FuncAddr funcAddr)
        {
            if (!Context.Store.Contains(funcAddr))
                throw new ArgumentException($"Runtime did not contain function address.");
            var funcInst = Context.Store[funcAddr];
            return funcInst.Id;
        }

        public FunctionType GetFunctionType(FuncAddr funcAddr)
        {
            if (!Context.Store.Contains(funcAddr))
                throw new ArgumentException($"Runtime did not contain function address.");
            var funcInst = Context.Store[funcAddr];
            return funcInst.Type;
        }

        public MemoryInstance BindHostMemory((string module, string entity) id, MemoryType memType)
        {
            Store.OpenTransaction();
            var memAddr = AllocateMemory(Store, memType);
            _entityBindings[id] = memAddr;
            Store.CommitTransaction();
            return Store[memAddr];
        }

        public GlobalInstance BindHostGlobal((string module, string entity) id, GlobalType globalType, Value val)
        {
            if (globalType.ContentType != val.Type)
                throw new ArgumentException(
                    $"Global {globalType.ContentType} must be defined with matching type value {val}");
            
            Store.OpenTransaction();
            var globAddr = AllocateGlobal(Store, globalType, val);
            _entityBindings[id] = globAddr;
            Store.CommitTransaction();
            return Store[globAddr];
        }

        public TagInstance BindHostTag((string module, string entity) id, DefType tagType)
        {
            Store.OpenTransaction();
            var tagAddr = AllocateTag(Store, tagType);
            _entityBindings[id] = tagAddr;
            Store.CommitTransaction();
            return Store[tagAddr];
        }

        public TableInstance BindHostTable((string module, string entity) id, TableType tableType, Value val)
        {
            if (tableType.ElementType != val.Type)
                throw new ArgumentException(
                    $"Table {tableType.ElementType} must be defined with matching element type value {val}");
            
            Store.OpenTransaction();
            var tableAddr = AllocateTable(Store, tableType, val);
            _entityBindings[id] = tableAddr;
            Store.CommitTransaction();
            return Store[tableAddr];
        }
    }
}