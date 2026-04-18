// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT;
using Wacs.WASIp1;

namespace Wacs.Transpiler.Cli
{
    /// <summary>
    /// Wires WASI preview1 imports into a transpiled module and invokes an
    /// entry-point export in-process.
    ///
    /// The WASI host functions are bound to the interpreter's WasmRuntime
    /// (standard WASIp1 bindings), then an ImportDispatcher proxy implements
    /// the transpiler's generated IImports interface by forwarding each
    /// method call through <c>runtime.CreateStackInvoker</c> — mixed mode:
    /// transpiled code calls interpreter for imports.
    ///
    /// Shared MemoryInstance between the transpiled ctx and the interpreter
    /// moduleInst is handled by the linker so WASI's fd_write / args_get /
    /// etc. see the same bytes the transpiled module reads and writes.
    /// </summary>
    public static class WasiRunner
    {
        public static WasiConfiguration BuildDefaultConfiguration(IEnumerable<string> programArgs)
        {
            return new WasiConfiguration
            {
                StandardInput = Console.OpenStandardInput(),
                StandardOutput = Console.OpenStandardOutput(),
                StandardError = Console.OpenStandardError(),
                Arguments = programArgs.ToList(),
                EnvironmentVariables = Environment.GetEnvironmentVariables()
                    .Cast<DictionaryEntry>()
                    .ToDictionary(de => de.Key.ToString()!,
                                  de => de.Value?.ToString() ?? string.Empty),
                HostRootDirectory = Directory.GetCurrentDirectory(),
            };
        }

        /// <summary>
        /// After the module has been instantiated + transpiled with WASI
        /// host functions already bound to the runtime, construct the Module
        /// class with an IImports proxy that forwards to the runtime's
        /// bound hosts, then invoke the named export.
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
                Console.Error.WriteLine("error: --wasi: transpiler produced no Module class");
                return 1;
            }

            object? importsProxy = null;
            if (result.ImportsInterface != null)
            {
                importsProxy = BuildImportsProxy(result, runtime, moduleInst);
                if (importsProxy == null)
                {
                    Console.Error.WriteLine("error: --wasi: could not build IImports proxy");
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
                Console.Error.WriteLine($"error: --wasi: module ctor threw: {tie.InnerException?.Message ?? tie.Message}");
                return 1;
            }

            if (module == null)
            {
                Console.Error.WriteLine("error: --wasi: Activator.CreateInstance returned null");
                return 1;
            }

            // Share MemoryInstance between the transpiled ctx and the
            // interpreter moduleInst so WASI host functions (which write via
            // ctx.DefaultMemory = Store[Frame.Module.MemAddrs[0]]) and the
            // transpiled code see the same bytes. InitializationHelper
            // allocated a fresh MemoryInstance per ctx.Memories slot; swap it
            // for the one already sitting in the runtime's Store. Data
            // segments are already copied into both — interpreter applied
            // them at InstantiateModule time, the transpiler applied them
            // during its own init — but only the interpreter's copy matters
            // after this patch since the fresh allocation gets collected.
            ShareMemoriesWithRuntime(module, result, runtime, moduleInst);

            var method = FindExportMethod(result, exportName);
            if (method == null)
            {
                var available = string.Join(", ", result.ExportMethods.Select(m => "\"" + m.WasmName + "\""));
                Console.Error.WriteLine(
                    $"error: --wasi: export '{exportName}' not found; available: [{available}]");
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
                if (inner is WasiExitException exit) return exit.ExitCode;
                Console.Error.WriteLine($"error: --wasi: {inner?.GetType().Name}: {inner?.Message ?? tie.Message}");
                if (verbose && inner != null)
                    Console.Error.WriteLine(inner.StackTrace);
                return 1;
            }
        }

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
            // Exports are public instance methods on the Module class; look up
            // by the CLR-sanitized name (export.Name).
            return result.ModuleClass.GetMethod(
                export.Name,
                BindingFlags.Public | BindingFlags.Instance);
        }

        private static object?[] BuildArgs(MethodInfo method)
        {
            var ps = method.GetParameters();
            var args = new object?[ps.Length];
            // WASI entry points (`_start`) take no args; other zero-arg exports
            // (`start`, `main` etc.) are fine too. Anything with params here
            // uses default values — caller should use --emit-main for typed
            // entry points that need CLI-parsed args.
            for (int i = 0; i < ps.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType
                    ? Activator.CreateInstance(ps[i].ParameterType)
                    : null;
            return args;
        }

        /// <summary>
        /// Build an IImports proxy whose methods forward each call through
        /// the interpreter's stack invoker for the matching bound host
        /// function. Same shape as the test harness's BuildImportsProxy
        /// (see Wacs.Transpiler.Test/AotSpecTests.cs) but for WASI only —
        /// no cross-module wrapper lookup needed.
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

                // Find the matching import in the module repr so we can look
                // up the bound host function by (module, entity).
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
                    handlers[importName] = _ => null; // unknown; return default
                    continue;
                }

                var capturedImport = matched;
                var capturedFuncType = funcType;
                // CreateStackInvoker doesn't change per-call — cache it.
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

                    // WASI host functions read ctx.DefaultMemory =
                    // Store[Frame.Module.MemAddrs[0]]. When invoked from our
                    // proxy (no prior WASM caller frame), Frame is the sentinel
                    // NullFrame and DefaultMemory dereferences null. Temporarily
                    // set Frame to one bound to moduleInst — the same module
                    // the transpiled code represents — so WASI reads the shared
                    // interpreter memory. We restore the prior Frame after.
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
                Console.Error.WriteLine($"error: --wasi: proxy creation failed: {ex.Message}");
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

        /// <summary>
        /// Signaled by WASI's proc_exit — let the CLI exit with the requested
        /// code rather than propagating the exception. (Wacs.WASIp1 throws a
        /// specific exception type; fall back to any exception named
        /// "WasiExitException" / "ProcExit" to stay tolerant of renames.)
        /// </summary>
        private class WasiExitException : Exception { public int ExitCode; }
    }
}
