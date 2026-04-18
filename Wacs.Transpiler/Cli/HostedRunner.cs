// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT;

namespace Wacs.Transpiler.Cli
{
    /// <summary>
    /// Runs a transpiled WASM module in-process with any number of
    /// <see cref="IBindable"/> host libraries wired into the runtime —
    /// WASI, a game-engine host, a custom syscall shim, whatever the caller
    /// passes in. The runtime owns the host bindings; an ImportDispatcher
    /// proxy implements the transpiler's generated IImports by forwarding
    /// each method call through <c>runtime.CreateStackInvoker</c> for the
    /// matching bound host (mixed mode: AOT code, interpreter imports).
    ///
    /// The transpiled module's linear memory is swapped for the interpreter's
    /// <see cref="MemoryInstance"/> (<c>Store[moduleInst.MemAddrs[i]]</c>) so
    /// host functions that read <c>ctx.DefaultMemory</c> (e.g. WASI's
    /// <c>fd_write</c>, <c>args_get</c>) see the exact same bytes the AOT
    /// code is reading and writing.
    /// </summary>
    public static class HostedRunner
    {
        /// <summary>
        /// Instantiate the transpiled Module class against the given
        /// interpreter moduleInst + runtime, share memory, and invoke the
        /// named export.
        ///
        /// Callers are expected to have bound any host libraries to the
        /// runtime BEFORE calling <see cref="WasmRuntime.InstantiateModule"/>.
        /// <see cref="BindingLoader"/> handles the <c>--bind</c> CLI case;
        /// hand-wired hosts can just call <c>host.BindToRuntime(runtime)</c>
        /// themselves.
        /// </summary>
        public static int Run(
            TranspilationResult result,
            WasmRuntime runtime,
            ModuleInstance moduleInst,
            string exportName,
            bool verbose)
        {
            if (result.ModuleClass == null)
            {
                Console.Error.WriteLine("error: host run: transpiler produced no Module class");
                return 1;
            }

            object? importsProxy = null;
            if (result.ImportsInterface != null)
            {
                importsProxy = BuildImportsProxy(result, runtime, moduleInst);
                if (importsProxy == null)
                {
                    Console.Error.WriteLine("error: host run: could not build IImports proxy");
                    return 1;
                }
            }

            object? module;
            try
            {
                module = importsProxy != null
                    ? Activator.CreateInstance(result.ModuleClass, importsProxy)
                    : Activator.CreateInstance(result.ModuleClass);
            }
            catch (TargetInvocationException tie)
            {
                Console.Error.WriteLine($"error: host run: module ctor threw: {tie.InnerException?.Message ?? tie.Message}");
                return 1;
            }

            if (module == null)
            {
                Console.Error.WriteLine("error: host run: Activator.CreateInstance returned null");
                return 1;
            }

            ShareMemoriesWithRuntime(module, result, runtime, moduleInst);

            var method = FindExportMethod(result, exportName);
            if (method == null)
            {
                var available = string.Join(", ", result.ExportMethods.Select(m => "\"" + m.WasmName + "\""));
                Console.Error.WriteLine(
                    $"error: host run: export '{exportName}' not found; available: [{available}]");
                return 1;
            }

            if (verbose)
                Console.WriteLine($"run           {result.ModuleClass.FullName}.{method.Name}()");

            try
            {
                var rv = method.Invoke(module, BuildArgs(method));
                if (rv is int exitCode) return exitCode;
                return 0;
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException;
                Console.Error.WriteLine($"error: host run: {inner?.GetType().Name}: {inner?.Message ?? tie.Message}");
                if (verbose && inner != null)
                    Console.Error.WriteLine(inner.StackTrace);
                return 1;
            }
        }

        /// <summary>
        /// Share <see cref="MemoryInstance"/> between the transpiled ctx and
        /// the interpreter moduleInst. The generated Module ctor's
        /// InitializationHelper allocated fresh memories and copied data
        /// segments into them; swap each slot for the one sitting in the
        /// runtime Store so host imports (reading via ctx.DefaultMemory) and
        /// the AOT code see the same bytes. The interpreter's
        /// InstantiateModule already copied data segments into its
        /// MemoryInstance, so after the swap no bytes are lost.
        /// </summary>
        private static void ShareMemoriesWithRuntime(
            object module, TranspilationResult result,
            WasmRuntime runtime, ModuleInstance moduleInst)
        {
            if (result.ModuleClass == null) return;
            var ctxField = result.ModuleClass.GetField(
                "_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
            if (ctxField == null) return;
            var ctx = ctxField.GetValue(module) as ThinContext;
            if (ctx == null) return;

            var store = runtime.RuntimeStore;
            for (int i = 0; i < ctx.Memories.Length; i++)
            {
                var idx = (MemIdx)(uint)i;
                if (!moduleInst.MemAddrs.Contains(idx)) break;
                var memAddr = moduleInst.MemAddrs[idx];
                if (store.Contains(memAddr))
                    ctx.Memories[i] = store[memAddr];
            }
        }

        private static MethodInfo? FindExportMethod(TranspilationResult result, string exportName)
        {
            var export = result.ExportMethods.FirstOrDefault(m =>
                m.WasmName == exportName || m.Name == exportName);
            if (export == null || result.ModuleClass == null) return null;
            return result.ModuleClass.GetMethod(
                export.Name,
                BindingFlags.Public | BindingFlags.Instance);
        }

        private static object?[] BuildArgs(MethodInfo method)
        {
            var ps = method.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType
                    ? Activator.CreateInstance(ps[i].ParameterType)
                    : null;
            return args;
        }

        /// <summary>
        /// Build an IImports proxy whose methods forward each call through
        /// the interpreter's stack invoker for the matching bound host
        /// function. Host bindings must already be registered on the runtime
        /// (any <see cref="IBindable"/> host library does this via its
        /// BindToRuntime implementation).
        /// </summary>
        private static object? BuildImportsProxy(
            TranspilationResult result, WasmRuntime runtime, ModuleInstance moduleInst)
        {
            if (result.ImportsInterface == null) return null;

            var handlers = new Dictionary<string, Func<object?[], object?>>();
            foreach (var importMethod in result.ImportMethods)
            {
                var importName = importMethod.Name;
                var funcType = importMethod.WasmType;

                Wacs.Core.Module.Import? matched = null;
                foreach (var imp in moduleInst.Repr.Imports)
                {
                    if (imp.Desc is not Wacs.Core.Module.ImportDesc.FuncDesc) continue;
                    var expected = InterfaceGenerator.SanitizeName($"{imp.ModuleName}_{imp.Name}");
                    if (expected != importName) continue;
                    matched = imp;
                    break;
                }

                if (matched == null)
                {
                    handlers[importName] = _ => null;
                    continue;
                }

                var capturedImport = matched;
                var capturedFuncType = funcType;
                Delegates.StackFunc? cachedInvoker = null;
                handlers[importName] = args =>
                {
                    if (cachedInvoker == null)
                    {
                        if (!runtime.TryGetExportedFunction(
                                (capturedImport.ModuleName, capturedImport.Name), out var addr))
                            return null;
                        cachedInvoker = runtime.CreateStackInvoker(addr);
                    }

                    // Host functions that read ctx.DefaultMemory need a Frame
                    // whose Module is moduleInst. Invoking a bound host via
                    // CreateStackInvoker standalone leaves Frame = NullFrame,
                    // which NREs on MemAddrs[0]. Temporarily set Frame, then
                    // restore after the call.
                    var ctx = runtime.ExecContext;
                    var savedFrame = ctx.Frame;
                    var frame = ctx.ReserveFrame(moduleInst, capturedFuncType.ResultType.Arity);
                    ctx.Frame = frame;
                    try
                    {
                        var valueArgs = ConvertArgsToValues(args);
                        var results = cachedInvoker(valueArgs);
                        if (results.Length == 0) return null;
                        return ConvertValueToClr(results[0], capturedFuncType.ResultType.Types[0]);
                    }
                    finally
                    {
                        ctx.Frame = savedFrame;
                    }
                };
            }

            try
            {
                return ImportDispatcher.Create(result.ImportsInterface, handlers);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: host run: proxy creation failed: {ex.Message}");
                return null;
            }
        }

        private static Value[] ConvertArgsToValues(object?[]? args)
        {
            if (args == null) return Array.Empty<Value>();
            var values = new Value[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                values[i] = args[i] switch
                {
                    Value v => v,
                    int iv => new Value(iv),
                    long lv => new Value(lv),
                    float fv => new Value(fv),
                    double dv => new Value(dv),
                    _ => default,
                };
            }
            return values;
        }

        private static object? ConvertValueToClr(Value val, ValType type) => type switch
        {
            ValType.I32 => val.Data.Int32,
            ValType.I64 => val.Data.Int64,
            ValType.F32 => val.Data.Float32,
            ValType.F64 => val.Data.Float64,
            _ => (object)val,
        };
    }
}
