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
        /// <summary>
        /// Optional cross-binding hook for releasing resource
        /// handles allocated outside an <c>IBindable</c>'s own
        /// resource tables. Used by the transpiler / wasip2
        /// direct-link path, where component-model resources
        /// (e.g. wasi-nn tensors) get allocated in
        /// <c>WasiPreview2Resources</c> / <c>ResourceContext</c>
        /// but the interpreter binding's <c>[resource-drop]X</c>
        /// only knows about the <c>IBindable</c>'s local table.
        /// Without this hook, large host-owned resources allocated
        /// during direct-link compute calls (e.g. multi-hundred-MiB
        /// logits tensors in the SLM workflow) accumulate in the
        /// direct-link table forever — the leak shows up as
        /// unbounded managed-heap growth across compute calls.
        ///
        /// <para>Set by the runtime-scope construction
        /// (<c>WasiPreview2RuntimeScope</c>) once the direct-link
        /// resources object is available. <c>IBindable</c>-side
        /// drop handlers call <c>ExternalResourceDrop?.Invoke(...)</c>
        /// after dropping from their own table — whichever table
        /// holds the entry actually releases it; the off-table
        /// call returns false silently.</para>
        /// </summary>
        public Action<Type, int>? ExternalResourceDrop { get; set; }

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

        public IFunctionInstance GetFunction(FuncAddr addr)
        {
            if (!GetExecContext().Store.Contains(addr))
                throw new WasmRuntimeException($"Runtime context did not contain function at address {addr.Value}");
            return GetExecContext().Store[addr];
        }

        // ==================================================================
        // Exported entity accessors (doc 1 §4.6, spec "resolution").
        //
        // Each exportable kind (Function / Memory / Table / Global / Tag) has
        // matching overloads:
        //   TryGetExported{Kind}(string entity, out T)
        //   TryGetExported{Kind}((string module, string entity), out T)
        //   GetExported{Kind}((string module, string entity)) -> T  (throws)
        //
        // Function returns FuncAddr (use CreateInvoker / GetFunction to
        // invoke or fetch the IFunctionInstance). The other kinds return
        // the *Instance type directly — they're immediately usable to read
        // memory bytes, inspect globals, etc., so there's no reason to route
        // callers through the Store indexer. See issue #63.
        // ==================================================================

        public bool TryGetExportedMemory(string entity, out MemoryInstance memory)
        {
            var addrs = _moduleInstances.SelectMany(m => m.Exports)
                .Where(e => e.Name == entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Memory>()
                .Select(m => m.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                memory = GetExecContext().Store[addrs[^1]];
                return true;
            }
            memory = null!;
            return false;
        }

        public bool TryGetExportedMemory((string module, string entity) id, out MemoryInstance memory)
        {
            if (GetBoundEntity(id) is MemAddr addr && GetExecContext().Store.Contains(addr))
            {
                memory = GetExecContext().Store[addr];
                return true;
            }
            var addrs = _moduleInstances
                .Where(m => m.Name == id.module)
                .SelectMany(m => m.Exports)
                .Where(e => e.Name == id.entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Memory>()
                .Select(m => m.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                memory = GetExecContext().Store[addrs[^1]];
                return true;
            }
            memory = null!;
            return false;
        }

        public MemoryInstance GetExportedMemory((string module, string entity) id)
        {
            if (TryGetExportedMemory(id, out var memory)) return memory;
            throw new UnboundEntityException(
                $"Memory {id} was not exported from any modules currently loaded in the runtime.");
        }

        public bool TryGetExportedTable(string entity, out TableInstance table)
        {
            var addrs = _moduleInstances.SelectMany(m => m.Exports)
                .Where(e => e.Name == entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Table>()
                .Select(t => t.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                table = GetExecContext().Store[addrs[^1]];
                return true;
            }
            table = null!;
            return false;
        }

        public bool TryGetExportedTable((string module, string entity) id, out TableInstance table)
        {
            if (GetBoundEntity(id) is TableAddr addr && GetExecContext().Store.Contains(addr))
            {
                table = GetExecContext().Store[addr];
                return true;
            }
            var addrs = _moduleInstances
                .Where(m => m.Name == id.module)
                .SelectMany(m => m.Exports)
                .Where(e => e.Name == id.entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Table>()
                .Select(t => t.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                table = GetExecContext().Store[addrs[^1]];
                return true;
            }
            table = null!;
            return false;
        }

        public TableInstance GetExportedTable((string module, string entity) id)
        {
            if (TryGetExportedTable(id, out var table)) return table;
            throw new UnboundEntityException(
                $"Table {id} was not exported from any modules currently loaded in the runtime.");
        }

        public bool TryGetExportedGlobal(string entity, out GlobalInstance global)
        {
            var addrs = _moduleInstances.SelectMany(m => m.Exports)
                .Where(e => e.Name == entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Global>()
                .Select(g => g.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                global = GetExecContext().Store[addrs[^1]];
                return true;
            }
            global = null!;
            return false;
        }

        public bool TryGetExportedGlobal((string module, string entity) id, out GlobalInstance global)
        {
            if (GetBoundEntity(id) is GlobalAddr addr && GetExecContext().Store.Contains(addr))
            {
                global = GetExecContext().Store[addr];
                return true;
            }
            var addrs = _moduleInstances
                .Where(m => m.Name == id.module)
                .SelectMany(m => m.Exports)
                .Where(e => e.Name == id.entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Global>()
                .Select(g => g.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                global = GetExecContext().Store[addrs[^1]];
                return true;
            }
            global = null!;
            return false;
        }

        public GlobalInstance GetExportedGlobal((string module, string entity) id)
        {
            if (TryGetExportedGlobal(id, out var global)) return global;
            throw new UnboundEntityException(
                $"Global {id} was not exported from any modules currently loaded in the runtime.");
        }

        public bool TryGetExportedTag(string entity, out TagInstance tag)
        {
            var addrs = _moduleInstances.SelectMany(m => m.Exports)
                .Where(e => e.Name == entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Tag>()
                .Select(t => t.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                tag = GetExecContext().Store[addrs[^1]];
                return true;
            }
            tag = null!;
            return false;
        }

        public bool TryGetExportedTag((string module, string entity) id, out TagInstance tag)
        {
            if (GetBoundEntity(id) is TagAddr addr && GetExecContext().Store.Contains(addr))
            {
                tag = GetExecContext().Store[addr];
                return true;
            }
            var addrs = _moduleInstances
                .Where(m => m.Name == id.module)
                .SelectMany(m => m.Exports)
                .Where(e => e.Name == id.entity)
                .Select(e => e.Value)
                .OfType<ExternalValue.Tag>()
                .Select(t => t.Address)
                .ToList();
            if (addrs.Count > 0 && GetExecContext().Store.Contains(addrs[^1]))
            {
                tag = GetExecContext().Store[addrs[^1]];
                return true;
            }
            tag = null!;
            return false;
        }

        public TagInstance GetExportedTag((string module, string entity) id)
        {
            if (TryGetExportedTag(id, out var tag)) return tag;
            throw new UnboundEntityException(
                $"Tag {id} was not exported from any modules currently loaded in the runtime.");
        }

        /// <summary>
        /// Replace the function at the given address with a different implementation.
        /// Used by the AOT transpiler to swap interpreter-backed functions with transpiled versions.
        /// </summary>
        public void ReplaceFunction(FuncAddr addr, IFunctionInstance replacement)
        {
            if (!GetExecContext().Store.Contains(addr))
                throw new WasmRuntimeException($"Runtime context did not contain function at address {addr.Value}");
            GetExecContext().Store.ReplaceFunction(addr, replacement);
        }

        private IAddress? GetBoundEntity((string module, string entity) id)
        {
            if (_entityBindings.TryGetValue(id, out var direct))
                return direct;

            // Version-tolerant fallback: when the lookup misses,
            // try with the trailing `@<version>` stripped from the
            // module string. The wasm Component Model treats minor
            // revisions of WASI as ABI-stable, so a guest built
            // against `wasi:cli/stdout@0.2.6` should bind to a host
            // package shipping `@0.2.8` — same shape, ABI-equivalent.
            // wasmtime / jco / wasmer do the same fuzzy match.
            //
            // Stripped entries land in the same dict keyed by the
            // stripped module name when BindHostFunction sees an
            // `@<version>` suffix; this is a fallback when only one
            // side carries the version annotation.
            int at = id.module.LastIndexOf('@');
            if (at < 0) return null;
            string stripped = id.module.Substring(0, at);

            // Try the stripped lookup directly (matches when host
            // bound under the un-versioned name).
            if (_entityBindings.TryGetValue((stripped, id.entity),
                    out var strippedHit))
                return strippedHit;

            // Final pass: the host bound under a DIFFERENT
            // @<version>. Scan keys for any that strip to the same
            // module + match the entity. O(n) on miss only; the
            // typical bound entity count is in the hundreds and
            // this branch fires at most once per failed import.
            foreach (var kv in _entityBindings)
            {
                if (kv.Key.Item2 != id.entity) continue;
                int kvAt = kv.Key.Item1.LastIndexOf('@');
                if (kvAt < 0) continue;
                if (string.CompareOrdinal(
                        kv.Key.Item1, 0, stripped, 0, stripped.Length) == 0
                    && kvAt == stripped.Length)
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// Enumerate every <c>(module, entity)</c> import currently
        /// bound to this runtime. Used by the validation layer to
        /// match a WIT contract against the actual binding
        /// manifest produced by <c>IBindable.BindToRuntime</c>
        /// calls.
        /// </summary>
        public IEnumerable<(string Module, string Entity)>
            EnumerateBoundEntities()
        {
            foreach (var key in _entityBindings.Keys)
                yield return key;
        }

        /// <summary>
        /// Inspect the canonical-ABI-lowered function signature
        /// recorded for a host-function binding. Returns
        /// <c>true</c> when <paramref name="id"/> is bound to a
        /// host function (skipping memory / table / global
        /// imports); <paramref name="type"/> carries the
        /// flat-lowered param + return wire types the runtime
        /// will dispatch through.
        /// </summary>
        public bool TryGetBoundHostFunctionType(
            (string module, string entity) id,
            out FunctionType type)
        {
            if (_entityBindings.TryGetValue(id, out var addr)
                && addr is FuncAddr fa)
            {
                type = Store[fa].Type;
                return true;
            }
            type = null!;
            return false;
        }

        /// <summary>
        /// Mark <paramref name="id"/> as provided by a transpiler
        /// direct-link bundle. Subsequent
        /// <see cref="BindHostFunction{TDelegate}"/> calls for the
        /// same <c>(module, entity)</c> pair silently no-op — the
        /// emitted IL hardcodes the call into the bundle's typed
        /// interface and bypasses the runtime entity registry, so
        /// any IBindable-style registration would shadow nothing
        /// and risk aliasing the resource-handle namespace across
        /// two independent registries (one per binding source).
        ///
        /// <para>Called by <c>ComponentTranspiler.TranspileSingleModule</c>
        /// during its import pre-pass: for every wasm import where
        /// the resolver matches a binding, mark the entity. Then
        /// the <c>configureImports</c> callback (which runs
        /// <c>WasiPreview2RuntimeScope</c> + <c>ApplyBindings</c>)
        /// can register handlers freely; coverage-overlapping
        /// registrations drop silently instead of inducing the
        /// registry-split observed in the wasi-nn SLM (round-10
        /// follow-up bisection).</para>
        /// </summary>
        public void MarkEntityProvidedByDirectLink(
            (string module, string entity) id)
        {
            _directLinkProvidedEntities.Add(id);
        }

        /// <summary>
        /// True when <paramref name="id"/> was previously marked
        /// via <see cref="MarkEntityProvidedByDirectLink"/>. Exposed
        /// for diagnostics; the runtime checks this internally on
        /// <see cref="BindHostFunction{TDelegate}"/>.
        /// </summary>
        public bool IsEntityProvidedByDirectLink(
            (string module, string entity) id)
            => _directLinkProvidedEntities.Contains(id);

        public void BindHostFunction<TDelegate>((string module, string entity) id, TDelegate func)
            where TDelegate : Delegate
        {
            // Direct-link coverage shadow: the transpiler's IL
            // bypasses the runtime entity registry for direct-link-
            // covered entities, so subsequent attempts to override
            // an existing entity binding for them would shadow
            // nothing useful at the dispatch path AND risk aliasing
            // the resource-handle namespace across two registries
            // if the override IS still invoked through some
            // fallback path (delegate dispatch into an IBindable
            // handler that allocates in its own table).
            //
            // The rule fires only when the entity is marked AND
            // already has a binding — i.e. on the second+ call.
            // The first registration (typically the trap-stub
            // placeholder from `ComponentImportStubs.RegisterAll`)
            // goes through unchanged so the runtime's
            // import-resolution validation
            // (`WasmRuntimeInstantiation` line ~169) can find
            // every import bound. Subsequent IBindable handlers
            // that try to override the trap-stub for marked
            // entities get dropped — the trap-stub stays as the
            // never-invoked placeholder while the direct-link IL
            // does the actual dispatch.
            if (_directLinkProvidedEntities.Contains(id)
                && _entityBindings.ContainsKey(id))
                return;

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

        // Bind a pre-built IFunctionInstance at a given (module, entity) name.
        // Used by recognized-import builtins (e.g. wasm:js-string) that need
        // operand-stack access for ref-typed params the delegate marshaler
        // doesn't cover.
        public void BindHostFunction((string module, string entity) id, IFunctionInstance func)
        {
            // Same shadow rule as the delegate overload — see the
            // detailed comment there. Only fires after a trap-stub
            // (or first-bound) placeholder is in `_entityBindings`,
            // so the trap-stub registration itself goes through.
            if (_directLinkProvidedEntities.Contains(id)
                && _entityBindings.ContainsKey(id))
                return;

            Store.OpenTransaction();
            var funcAddr = Store.AddFunction(func);
            Store.CommitTransaction();
            _entityBindings[id] = funcAddr;
        }

        public string GetFunctionName(FuncAddr funcAddr)
        {
            if (!GetExecContext().Store.Contains(funcAddr))
                throw new ArgumentException($"Runtime did not contain function address.");
            var funcInst = GetExecContext().Store[funcAddr];
            return funcInst.Id;
        }

        public FunctionType GetFunctionType(FuncAddr funcAddr)
        {
            if (!GetExecContext().Store.Contains(funcAddr))
                throw new ArgumentException($"Runtime did not contain function address.");
            var funcInst = GetExecContext().Store[funcAddr];
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