// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.WASIp1;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Wacs.Transpiler.Cli;          // HostedRunner (legacy ns inside Lib)
using Wacs.Transpiler.Hosting;
using Wacs.WASI.Preview1.Types;
using Wacs.WASI.Preview2.DependencyInjection;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs run` verb handler. Dispatches between single-file core
    /// wasm, multi-file core wasm composition, and component-mode
    /// based on input shape. Carries the full instrumentation surface
    /// (gas, profile, log-execution, stats, super, switch) inherited
    /// from the legacy <c>Wacs.Console</c> path, plus the multi-input
    /// + component-mode + bundle wiring inherited from
    /// <c>wasm-transpile</c>.
    /// </summary>
    public static class RunHandler
    {
        public static int Execute(RunOptions opts)
        {
            var files = (opts.Files ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (files.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs run requires at least one input file.");
                return 1;
            }
            foreach (var f in files)
            {
                if (!File.Exists(f))
                {
                    System.Console.Error.WriteLine($"error: input file not found: {f}");
                    return 1;
                }
            }

            // Component-mode detection on the LAST input (the entry-
            // point module). Components only support single-file
            // input today; multi-component composition rides through
            // --host-package against a pre-transpiled .dll.
            var entryFile = files[files.Count - 1];
            bool entryIsComponent;
            try
            {
                entryIsComponent = DetectComponent(entryFile);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    $"error: failed to read input header: {ex.Message}");
                return 1;
            }

            if (entryIsComponent && files.Count > 1)
            {
                System.Console.Error.WriteLine(
                    "error: multi-input mode supports core .wasm modules only; "
                    + "for cross-component composition use --host-package "
                    + "against a previously transpiled .dll.");
                return 1;
            }

            if (entryIsComponent)
                return ExecuteComponent(opts, entryFile);

            if (files.Count > 1)
                return ExecuteMultiCore(opts, files);

            return ExecuteSingleCore(opts, entryFile);
        }

        // ============================================================
        // Single-file core wasm (the legacy Wacs.Console path)
        // ============================================================

        private static int ExecuteSingleCore(RunOptions opts, string wasmPath)
        {
            // --native-memory pins the ambient storage mode read by
            // every WACS instantiation path (interpreter core path,
            // interpreter component path, transpiler Standard +
            // AotLinked emission). Restored on exit so subsequent
            // in-process callers (test harnesses, library hosts)
            // aren't affected.
            var prevStorage = AmbientRuntime.MemoryStorage;
            if (opts.NativeMemory)
                AmbientRuntime.MemoryStorage = MemoryStorageMode.NativePointer;
            try
            {
                return ExecuteSingleCoreInner(opts, wasmPath);
            }
            finally
            {
                AmbientRuntime.MemoryStorage = prevStorage;
            }
        }

        private static int ExecuteSingleCoreInner(RunOptions opts, string wasmPath)
        {
            string fileExtension = Path.GetExtension(wasmPath).ToLowerInvariant();
            if (fileExtension != ".wasm" && fileExtension != ".wat")
            {
                System.Console.Error.WriteLine(
                    $"Error: Invalid file extension: {fileExtension}. "
                    + "Expected .wasm or .wat (for .wast scripts use a spec runner).");
                return 1;
            }

            // Validate WASI directories. Accepts both bare paths and
            // wasmtime-style `host::guest` mount-pair syntax — the
            // existence check applies to the host-path side only.
            foreach (var dir in opts.Directories ?? Enumerable.Empty<string>())
            {
                var (hostPath, _) = SplitMount(dir);
                if (!Directory.Exists(hostPath))
                {
                    System.Console.Error.WriteLine(
                        $"Error: Directory not found: {hostPath}");
                    return 1;
                }
            }

            var envVars = new Dictionary<string, string>();
            foreach (var env in opts.EnvironmentVars ?? Enumerable.Empty<string>())
            {
                var parts = env.Split('=', 2);
                if (parts.Length != 2)
                {
                    System.Console.Error.WriteLine(
                        $"Error: Invalid environment variable format: {env}");
                    return 1;
                }
                envVars[parts[0]] = parts[1];
            }

            var runtime = new WasmRuntime();

            var parseTimer = new Stopwatch();
            if (opts.Verbose) parseTimer.Start();

            // Branch-hint metadata only matters when we go through the
            // transpiler. Interpreter / switch runtime ignore it, so
            // skip the parse work for those engines.
            Wacs.Core.BinaryModuleParser.ParseBranchHints = string.Equals(
                opts.Engine, "transpiler", StringComparison.OrdinalIgnoreCase);

            Wacs.Core.Module module;
            using (var fileStream = new FileStream(wasmPath, FileMode.Open))
            {
                module = fileExtension == ".wat"
                    ? Wacs.Core.Text.TextModuleParser.ParseWat(fileStream)
                    : BinaryModuleParser.ParseWasm(fileStream);
            }

            if (opts.Verbose)
            {
                parseTimer.Stop();
                System.Console.Error.WriteLine(
                    $"Parsing module took {parseTimer.ElapsedMilliseconds:#0.###}ms");
            }

            if (!opts.NoValidate)
                EmitValidationDiagnostics(module, wasmPath);

            // Stub `env.sayc` — preserves the legacy Wacs.Console default
            // host binding for hand-written demo modules.
            runtime.BindHostFunction<Action<char>>(("env", "sayc"),
                c => System.Console.Write(c));

            // WASI Preview 1 binding. The `--wasi` flag is implicit in
            // the legacy Wacs.Console behavior: WASI is always wired.
            // Keep that for backward compatibility; future revs could
            // make it explicit.
            var wasiArgs = new List<string> { wasmPath };
            wasiArgs.AddRange(opts.Args ?? Enumerable.Empty<string>());
            var wasiConfig = Wasi.DefaultConfiguration();
            wasiConfig.Arguments = wasiArgs;
            wasiConfig.EnvironmentVariables = envVars;
            wasiConfig.PreopenedDirectories = (opts.Directories
                    ?? Enumerable.Empty<string>())
                .Select(path => new PreopenedDirectory(wasiConfig, path))
                .ToList();
            using var wasi = new WASI.Preview1.Wasi(wasiConfig);
            wasi.BindToRuntime(runtime);

            // --bind: load custom IBindable host packages.
            ApplyBindings(opts, runtime);

            string moduleName = opts.ModuleName;
            if (opts.Verbose)
                System.Console.Error.WriteLine($"Instantiating Module {moduleName}");

            // --super / --switch wiring (interpreter-only). Mirrors the
            // legacy Wacs.Console mutually-exclusive logic.
            if (opts.UseSwitch)
            {
                runtime.UseSwitchRuntime = true;
                runtime.ExecContext.Attributes.UseSwitchSuperInstructions
                    = opts.SuperInstructions;
            }
            else if (opts.SuperInstructions)
            {
                runtime.SuperInstruction = true;
            }

            var modInst = runtime.InstantiateModule(module,
                new RuntimeOptions
                {
                    SkipModuleValidation = true,
                    TimeInstantiation = opts.Verbose,
                    MemoryStorage = opts.NativeMemory
                        ? MemoryStorageMode.NativePointer
                        : MemoryStorageMode.ManagedArray,
                });
            runtime.RegisterModule(moduleName, modInst);

            // Engine routing: --engine transpiler runs through the AOT
            // path with imports proxied back to the interpreter
            // (mixed-mode). Default `interpreter` stays in the
            // interpreter dispatch loop.
            if (string.Equals(opts.Engine, "transpiler",
                    StringComparison.OrdinalIgnoreCase))
                return RunViaTranspiler(opts, runtime, modInst);

            return InvokeInterpreterEntry(opts, runtime, modInst,
                moduleName, wasiArgs);
        }

        private static int InvokeInterpreterEntry(RunOptions opts,
            WasmRuntime runtime, ModuleInstance modInst,
            string moduleName, List<string> wasiArgs)
        {
            var callOptions = new InvokerOptions
            {
                LogGas = opts.LogGas,
                GasLimit = opts.GasLimit,
                LogProgressEvery = opts.LogProgressEvery,
                LogInstructionExecution = opts.LogExecution,
                CalculateLineNumbers = opts.CalculateLines,
                CollectStats = opts.Stats,
                SynchronousExecution = true,
            };

            // BasicModuleABI reactor convention: a module that
            // exports `_initialize` (no args, no result) but does
            // NOT export `_start` is a reactor library — runtimes
            // must invoke `_initialize` once before any other
            // export touches state, but never as the lone entry.
            // Detect-and-call here, ahead of the entry-point
            // selection below; falls through to --call (or the
            // helpful "this is a reactor" error) once init runs.
            bool hasStart = runtime.TryGetExportedFunction(
                (moduleName, "_start"), out _);
            if (!hasStart && runtime.TryGetExportedFunction(
                    (moduleName, "_initialize"), out var initAddr))
            {
                if (opts.Verbose)
                    System.Console.Error.WriteLine(
                        "Calling _initialize (reactor module per BasicModuleABI)");
                var initCaller = runtime.CreateInvokerAction(initAddr, callOptions);
                try { initCaller(); }
                catch (TrapException exc)
                {
                    System.Console.Error.WriteLine(exc);
                    return 1;
                }
                catch (SignalException exc)
                {
                    if (opts.Verbose)
                        System.Console.Error.WriteLine(
                            $"{((ErrNo)exc.Signal).HumanReadable()}");
                    return exc.Signal;
                }
                if (string.IsNullOrEmpty(opts.Call))
                {
                    System.Console.Error.WriteLine(
                        "error: module is a reactor (exports _initialize but "
                        + "not _start). Pass --call <export> to invoke a "
                        + "specific function.");
                    return 1;
                }
                // Fall through to --call dispatch below.
            }

            // Entry-point selection priority:
            //   1. WASM start section (modInst.StartFunc)
            //   2. _start export (WASI command convention)
            //   3. --call <fn> explicit invocation
            if (modInst.StartFunc != null)
            {
                var caller = runtime.CreateInvokerAction(
                    modInst.StartFunc, callOptions);
                var name = runtime.GetFunctionName(modInst.StartFunc);
                if (opts.Verbose)
                    System.Console.Error.WriteLine($"Executing wasm function {name}");

                using IDisposable _ = opts.Profile
                    ? new ProfilingSession() : new NoOpProfilingSession();
                try { caller(); }
                catch (TrapException exc)
                {
                    System.Console.Error.WriteLine(exc);
                    return 1;
                }
                catch (SignalException exc)
                {
                    if (opts.Verbose)
                        System.Console.Error.WriteLine($"{exc.HumanReadable}");
                    return exc.Signal;
                }
                return 0;
            }

            if (runtime.TryGetExportedFunction((moduleName, "_start"),
                    out var startAddr))
            {
                if (opts.Verbose) System.Console.Error.WriteLine("Calling start");
                var caller = runtime.CreateInvokerAction(startAddr, callOptions);

                using IDisposable _ = opts.Profile
                    ? new ProfilingSession() : new NoOpProfilingSession();
                try { caller(); }
                catch (TrapException exc)
                {
                    System.Console.Error.WriteLine(exc);
                    return 1;
                }
                catch (SignalException exc)
                {
                    ErrNo sig = (ErrNo)exc.Signal;
                    if (opts.Verbose)
                        System.Console.Error.WriteLine($"{sig.HumanReadable()}");
                    return exc.Signal;
                }
                return 0;
            }

            if (!string.IsNullOrEmpty(opts.Call) &&
                runtime.TryGetExportedFunction((moduleName, opts.Call),
                    out var invokeAddr))
            {
                if (opts.Verbose)
                    System.Console.Error.WriteLine($"Calling {opts.Call}");

                var caller = runtime.CreateStackInvoker(invokeAddr, callOptions);
                using IDisposable _ = opts.Profile
                    ? new ProfilingSession() : new NoOpProfilingSession();
                try
                {
                    var type = runtime.GetFunctionType(invokeAddr);
                    var provided = wasiArgs.Skip(1).ToList();
                    if (type.ParameterTypes.Arity != provided.Count)
                    {
                        var pStrs = string.Join(" ", provided);
                        System.Console.Error.WriteLine(
                            $"Number of parameters [{pStrs}] != "
                            + $"function[{opts.Call}] {type.ParameterTypes.Arity}");
                        return 1;
                    }

                    var pVals = new Value[provided.Count];
                    for (int i = 0; i < provided.Count; i++)
                        pVals[i] = new Value(type.ParameterTypes.Types[i], provided[i]);

                    Value[] result = caller(pVals);
                    System.Console.WriteLine(
                        $"Result:[{string.Join(" ", result)}]");
                }
                catch (TrapException exc)
                {
                    System.Console.Error.WriteLine(exc);
                    return 1;
                }
                catch (SignalException exc)
                {
                    ErrNo sig = (ErrNo)exc.Signal;
                    if (opts.Verbose)
                        System.Console.Error.WriteLine($"{sig.HumanReadable()}");
                    return exc.Signal;
                }
                return 0;
            }

            return 0;
        }

        // ============================================================
        // Multi-file core wasm (linker composition)
        // ============================================================

        private static int ExecuteMultiCore(RunOptions opts, List<string> files)
        {
            if (opts.Verbose)
            {
                System.Console.Error.WriteLine($"engine        {opts.Engine}");
                System.Console.Error.WriteLine($"modules       {files.Count}");
            }

            var runtime = new WasmRuntime();
            var disposables = new List<IDisposable>();

            // --wasi / --bind apply to the SHARED runtime — any module's
            // host imports get satisfied by the same set.
            if (opts.Wasi)
            {
                var wasiCfg = Wasi.DefaultConfiguration();
                wasiCfg.Arguments = new List<string>
                    { Path.GetFileName(files[files.Count - 1]) };
                wasiCfg.Arguments.AddRange(opts.Args ?? Enumerable.Empty<string>());
                var wasiBinding = new WASI.Preview1.Wasi(wasiCfg);
                wasiBinding.BindToRuntime(runtime);
                disposables.Add(wasiBinding);
            }
            ApplyBindings(opts, runtime, disposables);

            var parsed = new List<(string Name, Wacs.Core.Module M, ModuleInstance Inst)>();
            foreach (var inputPath in files)
            {
                var name = Path.GetFileNameWithoutExtension(inputPath);
                Wacs.Core.Module m;
                ModuleInstance inst;
                try
                {
                    using var fs = new FileStream(inputPath, FileMode.Open,
                        FileAccess.Read);
                    m = BinaryModuleParser.ParseWasm(fs);
                    inst = runtime.InstantiateModule(m,
                        new RuntimeOptions
                        {
                            MemoryStorage = opts.NativeMemory
                                ? MemoryStorageMode.NativePointer
                                : MemoryStorageMode.ManagedArray,
                        });
                    runtime.RegisterModule(name, inst);
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        $"error: failed to parse/instantiate '{name}': {ex.Message}");
                    foreach (var d in disposables) d.Dispose();
                    return 1;
                }
                parsed.Add((name, m, inst));
                if (opts.Verbose)
                    System.Console.Error.WriteLine($"registered    {name}");
            }

            // Multi-input transpiler-engine support is a follow-up.
            // For now, --engine transpiler with multiple inputs falls
            // back to the linker-composition interpreter dispatch.
            // (The build verb already supports multi-input transpile +
            // save; the run path will catch up in v1.1.)
            if (string.Equals(opts.Engine, "transpiler",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (opts.Verbose)
                    System.Console.Error.WriteLine(
                        "engine note: multi-input --engine transpiler "
                        + "in `run` falls back to interpreter dispatch; "
                        + "use `wacs build` for save-then-load.");
            }

            // Run the chosen export on the LAST module via the
            // interpreter's stack invoker. Trailing args parsed as
            // primitive scalars per the function type.
            int exit = 0;
            try
            {
                var last = parsed[parsed.Count - 1];
                string entry = !string.IsNullOrEmpty(opts.Call)
                    ? opts.Call : "_start";
                if (!runtime.TryGetExportedFunction((last.Name, entry),
                        out var addr))
                {
                    System.Console.Error.WriteLine(
                        $"error: export '{entry}' not found on '{last.Name}'");
                    exit = 1;
                }
                else
                {
                    var invoker = runtime.CreateStackInvoker(addr);
                    var ftype = runtime.GetFunctionType(addr);
                    var args = ParseInterpreterArgs(
                        (opts.Args ?? Enumerable.Empty<string>()).ToArray(),
                        ftype.ParameterTypes.Types);
                    var results = invoker(args);
                    if (results.Length == 1)
                        System.Console.WriteLine(FormatValue(results[0]));
                }
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"error: interpreter run failed: {ex.Message}");
                exit = 1;
            }
            finally
            {
                foreach (var d in disposables) d.Dispose();
            }
            return exit;
        }

        // ============================================================
        // Component-mode (single file)
        // ============================================================

        private static int ExecuteComponent(RunOptions opts, string componentPath)
        {
            // Pin the storage mode for the duration of the run.
            // Both the interpreter component path
            // (Wacs.ComponentModel.Runtime.ComponentInstance.Instantiate)
            // and the transpiled path (ExecuteComponentTranspiled →
            // InitializationHelper, and the AotLinked emission's
            // memory-array IL) observe AmbientRuntime.MemoryStorage at
            // memory-alloc time. Restored on return so other in-process
            // callers aren't affected.
            var prevStorage = AmbientRuntime.MemoryStorage;
            if (opts.NativeMemory)
                AmbientRuntime.MemoryStorage = MemoryStorageMode.NativePointer;
            try
            {
                return ExecuteComponentInner(opts, componentPath);
            }
            finally
            {
                AmbientRuntime.MemoryStorage = prevStorage;
            }
        }

        private static int ExecuteComponentInner(RunOptions opts, string componentPath)
        {
            // --wasip2 / --host-package implies the transpiler engine
            // for components — those flags route through the typed
            // bundle which is a transpile-time concept. Without host
            // packages the interpreter path can handle imports-less
            // or caller-bound-imports components.
            bool hasHostPackages = opts.Wasip2
                || (opts.HostPackage != null && opts.HostPackage.Any());
            bool useTranspiler = hasHostPackages
                || string.Equals(opts.Engine, "transpiler",
                    StringComparison.OrdinalIgnoreCase);

            if (!useTranspiler)
            {
                // Interpreter component path. The configureImports
                // callback gives us the underlying WasmRuntime —
                // honor `--bind` here so custom IBindable host
                // packages can satisfy component imports the same
                // way they do on the core paths.
                var bytes = File.ReadAllBytes(componentPath);
                var ci = Wacs.ComponentModel.Runtime.ComponentInstance
                    .Instantiate(bytes, rt => ApplyBindings(opts, rt));

                string entry = !string.IsNullOrEmpty(opts.Call)
                    ? opts.Call : "_start";
                try
                {
                    var result = ci.Invoke(entry,
                        ParseComponentInvokeArgs(
                            (opts.Args ?? Enumerable.Empty<string>())
                                .ToArray()));
                    if (result != null)
                        System.Console.WriteLine(result);
                }
                catch (ArgumentException ex)
                    when (ex.ParamName == "exportName")
                {
                    System.Console.Error.WriteLine(
                        $"error: component export '{entry}' not found.");
                    return 1;
                }
                return 0;
            }

            // Transpiler engine: build the resolver options + call
            // ComponentTranspiler.TranspileSingleModule, then invoke
            // the chosen export through the generated module.
            return ExecuteComponentTranspiled(opts, componentPath);
        }

        private static int ExecuteComponentTranspiled(RunOptions opts,
            string componentPath)
        {
            var hostPackages = ResolveHostPackages(opts);
            var tOpts = BuildTranspilerOptions(opts);
            tOpts.HostPackages = hostPackages;

            // The wasip2 DI scope is constructed inside
            // configureImports (where the runtime first becomes
            // available) and held across both transpile and
            // dispatch. The scope's Linker resolution registers
            // every *Bindings.BindToRuntime against the runtime
            // so direct-link-emit-can't-handle-this-shape imports
            // (e.g. wasi:filesystem/preopens.get-directories,
            // wasi:cli/environment.get-environment, headers.entries,
            // tcp-socket.accept) fall back to BindHostFunction
            // dispatch instead of zero-filling silently.
            WasiPreview2RuntimeScope? scope = null;
            var preopens = ParsePreopenMounts(opts.Directories);

            try
            {
                TranspilationResult result;
                try
                {
                    using var fs = new FileStream(componentPath, FileMode.Open,
                        FileAccess.Read);
                    result = ComponentTranspiler.TranspileSingleModule(
                        fs,
                        assemblyNamespace: "WacsRunComponent",
                        moduleName: "RunModule",
                        options: tOpts,
                        configureImports: rt =>
                        {
                            using var fs2 = new FileStream(componentPath,
                                FileMode.Open, FileAccess.Read);
                            var parsed = ComponentTranspiler.Parse(fs2);
                            if (parsed.CoreModules.Count == 0) return;
                            int primary = parsed.CoreModules.Count == 1 ? 0
                                : (Wacs.ComponentModel.Runtime.ComponentInstance
                                    .FindPrimaryCoreModuleIdx(parsed.Component)
                                  ?? 0);
                            ComponentImportStubs.RegisterAll(rt,
                                parsed.CoreModules[primary]);

                            // Pre-load `--bind` assemblies into
                            // AppDomain BEFORE scope construction so
                            // the wasip2 DI scope's reflective
                            // backend auto-wire (which walks
                            // AppDomain on `Assembly.Load(name)`
                            // miss — round 21 fix) can discover
                            // them. Without this preload the
                            // auto-wire's `TryLoadAssembly` returns
                            // null for `--bind <path-to-LlamaSharp.dll>`
                            // because `ApplyBindings` (which fires
                            // `Assembly.LoadFrom`) hasn't run yet.
                            // Closes gap 26 (round-21 verification:
                            // ordering bug).
                            //
                            // The actual `BindToRuntime` calls
                            // still defer to `ApplyBindings` below
                            // — the preload only populates
                            // AppDomain. `BindingLoader.LoadAssembly`
                            // is idempotent (`Assembly.LoadFrom` on
                            // a path returns the cached Assembly
                            // for that path) so the second-pass
                            // load is a no-op.
                            PreloadBindAssemblies(opts);

                            // Build the wasip2 scope BEFORE
                            // ApplyBindings so wasip2's
                            // BindToRuntime registrations land
                            // on top of the trap-stubs but BELOW
                            // any `--bind` shim that wants to
                            // override (last write wins via
                            // BindHostFunction's normal
                            // semantics).
                            if (opts.Wasip2)
                            {
                                scope = new WasiPreview2RuntimeScope(
                                    runtime: rt,
                                    preopens: preopens);
                            }

                            // --bind runs AFTER scope construction
                            // so an explicit IBindable.BindHostFunction
                            // overrides the default trap-stub OR the
                            // wasip2 binding for that import.
                            ApplyBindings(opts, rt);
                        });
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        $"error: component transpilation failed: {ex.Message}");
                    return 1;
                }

                // Dispatch through the same machinery `wacs build --emit-main`
                // would emit, so behavior matches the saved-and-loaded path.
                // When `--call` is unset, ComponentMainHost auto-resolves
                // the command-component entry (`wasi:cli/run@<version>#run`)
                // before falling back to `_start`. Matches what wasmtime,
                // jco, and wasmer do for stock command components.
                string? entry = string.IsNullOrEmpty(opts.Call) ? null : opts.Call;
                try
                {
                    return ComponentMainHost.Run(result.ModuleClass!,
                        (opts.Args ?? Enumerable.Empty<string>()).ToArray(),
                        entry,
                        prebuiltBundle: scope?.Bundle,
                        prebuiltResources: scope?.Resources);
                }
                catch (Exception ex)
                {
                    // Unwrap reflection-invoke wrappers so the diagnostic
                    // surfaces the actual cause (an unbound import, a
                    // canonical-ABI mismatch, etc.) instead of the
                    // useless "Exception has been thrown by the target
                    // of an invocation." outer message.
                    var inner = ex;
                    while (inner is System.Reflection.TargetInvocationException tie
                           && tie.InnerException != null)
                        inner = tie.InnerException;
                    System.Console.Error.WriteLine(
                        $"error: component run failed: {inner.Message}");
                    if (opts.Verbose)
                        System.Console.Error.WriteLine(inner);
                    return 1;
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }

        // ============================================================
        // Transpiler-engine path for single-file core wasm
        // ============================================================

        private static int RunViaTranspiler(RunOptions opts,
            WasmRuntime runtime, ModuleInstance modInst)
        {
            var tOpts = BuildTranspilerOptions(opts);

            if (opts.Verbose)
                System.Console.Error.WriteLine(
                    $"AOT: transpiling (simd={tOpts.Simd}, "
                    + $"tail_calls={tOpts.EmitTailCallPrefix}, "
                    + $"max_fn_size={tOpts.MaxFunctionSize}, "
                    + $"data_storage={tOpts.DataStorage})");

            var transpiler = new ModuleTranspiler("WacsRunAot", tOpts);
            TranspilationResult result;
            try
            {
                result = transpiler.Transpile(modInst, runtime, "WasmModule");
            }
            catch (Exception exc)
            {
                System.Console.Error.WriteLine(
                    $"error: --engine transpiler: transpile failed: {exc.Message}");
                if (opts.Verbose) System.Console.Error.WriteLine(exc);
                return 1;
            }

            string entryPoint = !string.IsNullOrEmpty(opts.Call)
                ? opts.Call
                : (modInst.StartFunc != null
                    ? runtime.GetFunctionName(modInst.StartFunc)
                    : "_start");

            return HostedRunner.Run(result, runtime, modInst,
                entryPoint, opts.Verbose);
        }

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>
        /// Apply <c>--bind</c> assemblies to <paramref name="runtime"/>.
        /// Each assembly is loaded via <see cref="BindingLoader"/>,
        /// every <c>IBindable</c> it exposes calls <c>BindToRuntime</c>,
        /// and any <c>IDisposable</c> bindings get registered with
        /// <paramref name="disposables"/> for end-of-run cleanup.
        ///
        /// <para>Honored on every dispatch path — core single-module
        /// (<c>ExecuteCore</c>), multi-core (<c>ExecuteMultiCore</c>),
        /// and both component paths (<c>ExecuteComponent</c>,
        /// <c>ExecuteComponentTranspiled</c>) — so symmetric to
        /// <c>--wasi</c>: explicit host bindings are available
        /// regardless of module shape. On the component-transpiler
        /// path bindings run AFTER the default trap-stub
        /// registration so they override the stubs for any imports
        /// they cover.</para>
        /// </summary>
        // Pre-load `--bind` assemblies (and the shorthand-flag
        // siblings) into AppDomain WITHOUT activating any
        // `IBindable` types. Used by the component-transpiler
        // path so the wasip2 DI scope's reflective backend
        // auto-wire — which walks AppDomain when
        // `Assembly.Load(name)` misses (round 21) — can discover
        // `Assembly.LoadFrom`'d packages before scope
        // construction (gap 26). The actual `BindToRuntime` calls
        // still happen later via `ApplyBindings`; the load step
        // is idempotent so the second-pass `LoadFromAssembly`
        // doesn't reload.
        //
        // Failures here are silent — diagnostic / surface-area
        // belongs to `ApplyBindings` (which surfaces a clear
        // `binding assembly not found` if the path / name
        // doesn't resolve). Catching here just avoids tearing
        // down scope construction on a bad `--bind` entry.
        private static void PreloadBindAssemblies(RunOptions opts)
        {
            var paths = new List<string>();
            if (opts.WasiNN) paths.Add("Wacs.WASI.NN.OnnxRuntime");
            if (opts.WasiThreads) paths.Add("Wacs.WASI.Threads");
            if (opts.Bind != null) paths.AddRange(opts.Bind);
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try { BindingLoader.LoadAssembly(p.Trim()); }
                catch { /* surface via ApplyBindings */ }
            }
        }

        private static void ApplyBindings(RunOptions opts,
            WasmRuntime runtime, List<IDisposable>? disposables = null)
        {
            // Compose built-in shorthands (--wasi-nn, --wasi-threads)
            // with the explicit --bind list. Shorthands load first
            // so an explicit --bind for the same package can override
            // the default wiring (BindHostFunction's
            // last-write-wins semantics).
            //
            // No name-based carve-outs for direct-link-covered
            // entities — `WasmRuntime.BindHostFunction` silently
            // no-ops registrations for any (module, entity) pair the
            // transpiler's pre-pass marked as provided by a
            // direct-link bundle. So `--wasip2 --wasi-nn` can still
            // load the WASI.NN IBindable; the WitBindings handlers
            // for entities the bundle covers (graph-funcs,
            // tensor.*, etc.) drop silently while handlers for
            // bundle-uncovered entities (e.g. the legacy WITX ABI
            // under wasi_ephemeral_nn) still register normally. The
            // architectural rule lives in the runtime, not the CLI.
            var paths = new List<string>();
            if (opts.WasiNN) paths.Add("Wacs.WASI.NN.OnnxRuntime");
            if (opts.WasiThreads) paths.Add("Wacs.WASI.Threads");
            if (opts.Bind != null) paths.AddRange(opts.Bind);

            foreach (var asmPath in paths)
            {
                var loaded = BindingLoader.LoadFromAssembly(asmPath);
                foreach (var b in loaded)
                {
                    b.BindToRuntime(runtime);
                    if (disposables != null && b is IDisposable d)
                        disposables.Add(d);
                }
                if (opts.Verbose)
                    System.Console.Error.WriteLine(
                        "bind          " + asmPath + " -> "
                        + loaded.Count + " binding(s)");
            }
        }

        /// <summary>
        /// Split a `--dir` entry into (hostPath, guestPath). Accepts
        /// the wasmtime-style `host::guest` form; a bare path mounts
        /// at `/<basename>` so a guest's canonical-absolute-path read
        /// (`/models/x.txt`) works against `wacs run -d models`.
        /// Matches Preview1's existing guest-path-rooting behavior so
        /// the same flag form works on both engines.
        /// </summary>
        private static (string hostPath, string guestPath) SplitMount(string raw)
        {
            int sep = raw.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0)
                return (raw.Substring(0, sep), raw.Substring(sep + 2));
            var trimmed = raw.TrimEnd('/', '\\');
            if (trimmed.StartsWith("./", StringComparison.Ordinal))
                trimmed = trimmed.Substring(2);
            return (raw, "/" + Path.GetFileName(trimmed));
        }

        private static (string hostPath, string guestPath)[] ParsePreopenMounts(
            IEnumerable<string>? dirs)
        {
            if (dirs == null) return Array.Empty<(string, string)>();
            return dirs.Select(SplitMount).ToArray();
        }

        private static bool DetectComponent(string path)
        {
            using var fs = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[8];
            int read = 0;
            while (read < header.Length)
            {
                int n = fs.Read(header.Slice(read));
                if (n <= 0) break;
                read += n;
            }
            return read == header.Length
                && ComponentBinaryParser.IsComponentHeader(header);
        }

        private static IReadOnlyList<System.Reflection.Assembly>
            ResolveHostPackages(RunOptions opts)
        {
            var names = new List<string>();
            if (opts.Wasip2)
            {
                names.Add("Wacs.WASI.Preview2");
                // SourceGen-shape impl classes (WasiPreview2Bundle's
                // resource impls) live in the DI sibling. The
                // resolver's `TryFindResourceImpl` AppDomain fallback
                // covers the case where DI isn't on this list, but
                // listing it explicitly avoids a stale-cache window
                // before the runtime scope loads the assembly. Gap 23
                // (round-17 verification) traced to the same shape
                // for the wasi-nn case.
                names.Add("Wacs.WASI.Preview2.DependencyInjection");
            }
            if (opts.WasiNN)
            {
                names.Add("Wacs.WASI.NN");
                // Tensor, Graph, GraphExecutionContext — the
                // SourceGen-shape impl classes resolved by
                // `TryFindResourceImpl` for [constructor]X direct-
                // link emit. Without this, [constructor]tensor
                // falls back to delegate dispatch and returns
                // handle 0 to the guest (gap 23 / round-17
                // verification).
                names.Add("Wacs.WASI.NN.DependencyInjection");
            }
            // --bind that resolves to a Wacs.WASI.NN.* sibling
            // (LlamaSharp / MLNet) implicitly needs the same DI +
            // typed-surface packages on host-packages — otherwise
            // the resolver ends up with incomplete WitSource
            // coverage and post-compute lifts trap with
            // out-of-bounds memory access (gap 24a, round-19
            // sibling observation). Mirrors the round-18 plumbing
            // for `--wasi-nn` (the OnnxRuntime case).
            if (BindReferencesWasiNNFamily(opts))
            {
                if (!names.Contains("Wacs.WASI.NN"))
                    names.Add("Wacs.WASI.NN");
                if (!names.Contains("Wacs.WASI.NN.DependencyInjection"))
                    names.Add("Wacs.WASI.NN.DependencyInjection");
            }
            foreach (var n in opts.HostPackage ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                names.Add(n.Trim());
            }
            if (names.Count == 0)
                return Array.Empty<System.Reflection.Assembly>();

            var asms = new List<System.Reflection.Assembly>(names.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in names)
            {
                if (!seen.Add(n)) continue;
                System.Reflection.Assembly asm;
                try
                {
                    asm = File.Exists(n)
                        ? System.Reflection.Assembly.LoadFrom(Path.GetFullPath(n))
                        : System.Reflection.Assembly.Load(n);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"--host-package: failed to load '{n}': {ex.Message}");
                }
                asms.Add(asm);
            }
            return asms;
        }

        // True when any --bind entry references a
        // `Wacs.WASI.NN.*` assembly — by name (`--bind
        // Wacs.WASI.NN.LlamaSharp`) or by file path whose basename
        // starts with that prefix (`--bind /.../Wacs.WASI.NN.MLNet.dll`).
        // Used to auto-pull the WASI.NN typed-surface + DI sibling
        // packages onto host-packages so the resolver has complete
        // WitSource coverage for the family. Sub-gap 24a
        // (round-19 sibling observation).
        private static bool BindReferencesWasiNNFamily(RunOptions opts)
        {
            foreach (var entry in opts.Bind ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var trimmed = entry.Trim();
                // Path form: take basename without .dll suffix.
                var ident = File.Exists(trimmed)
                    ? Path.GetFileNameWithoutExtension(trimmed)
                    : trimmed;
                if (ident == null) continue;
                if (ident.StartsWith("Wacs.WASI.NN.",
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static TranspilerOptions BuildTranspilerOptions(RunOptions opts)
        {
            return new TranspilerOptions
            {
                Simd = ParseSimdStrategy(opts.Simd),
                EmitTailCallPrefix = !opts.NoTailCalls,
                MaxFunctionSize = opts.MaxFnSize,
                DataStorage = ParseDataStorage(opts.DataStorage),
            };
        }

        private static SimdStrategy ParseSimdStrategy(string value) => value switch
        {
            "interpreter" => SimdStrategy.InterpreterDispatch,
            "intrinsics" => SimdStrategy.HardwareIntrinsics,
            _ => SimdStrategy.ScalarReference,
        };

        private static DataSegmentStorage ParseDataStorage(string value) => value switch
        {
            "raw" => DataSegmentStorage.RawResource,
            "static" => DataSegmentStorage.StaticArrays,
            _ => DataSegmentStorage.CompressedResource,
        };

        private static Value[] ParseInterpreterArgs(string[] argv,
            Wacs.Core.Types.Defs.ValType[] paramTypes)
        {
            var vals = new Value[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
            {
                var s = i < argv.Length ? argv[i] : "0";
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                vals[i] = paramTypes[i] switch
                {
                    Wacs.Core.Types.Defs.ValType.I32 => new Value(int.Parse(s, ic)),
                    Wacs.Core.Types.Defs.ValType.I64 => new Value(long.Parse(s, ic)),
                    Wacs.Core.Types.Defs.ValType.F32 => new Value(float.Parse(s, ic)),
                    Wacs.Core.Types.Defs.ValType.F64 => new Value(double.Parse(s, ic)),
                    _ => throw new ArgumentException(
                        $"unsupported interpreter arg type {paramTypes[i]}"),
                };
            }
            return vals;
        }

        private static object?[] ParseComponentInvokeArgs(string[] argv)
        {
            // ComponentInstance.Invoke takes object?[] with the user's
            // intended types. Without function-type introspection,
            // pass strings through as-is — the user can supply
            // pre-coerced values for non-string params via env helpers.
            var args = new object?[argv.Length];
            for (int i = 0; i < argv.Length; i++) args[i] = argv[i];
            return args;
        }

        private static string FormatValue(Value v)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            return v.Type switch
            {
                Wacs.Core.Types.Defs.ValType.I32 => v.Data.Int32.ToString(ic),
                Wacs.Core.Types.Defs.ValType.I64 => v.Data.Int64.ToString(ic),
                Wacs.Core.Types.Defs.ValType.F32 => v.Data.Float32.ToString(ic),
                Wacs.Core.Types.Defs.ValType.F64 => v.Data.Float64.ToString(ic),
                _ => v.ToString()!,
            };
        }

        private static void EmitValidationDiagnostics(Wacs.Core.Module module,
            string wasmPath)
        {
            var validationResult = module.Validate();
            var funcsToRender = new HashSet<(FuncIdx, string)>();
            foreach (var error in validationResult.Errors)
            {
                if (funcsToRender.Count > 100) break;
                if (error.Severity != Severity.Warning
                    && error.Severity != Severity.Error) continue;

                if (error.ErrorMessage.StartsWith("Function["))
                {
                    var parts = error.ErrorMessage.Split(":");
                    var path = parts[0];
                    var msg = string.Join(":", parts[1..]);

                    var (line, code) = module.CalculateLine(path);
                    if (!string.IsNullOrWhiteSpace(code)) code = $" ({code})";
                    var (fline, _) = module.CalculateLine(path,
                        functionRelative: true);

                    System.Console.Error.WriteLine(
                        $"Validation {error.Severity}.{msg}");
                    System.Console.Error.WriteLine($"    {path}");
                    System.Console.Error.WriteLine(
                        $"    at{code} in {wasmPath}:line {line} ({fline})");
                    System.Console.Error.WriteLine();

                    FuncIdx fIdx = ModuleRenderer.GetFuncIdx(path);
                    string funcId = ModuleRenderer.ChopFunctionId(path);
                    funcsToRender.Add((fIdx, funcId));
                }
                else
                {
                    System.Console.Error.WriteLine(
                        $"Validation {error.Severity}: {error.ErrorMessage}");
                }
            }

            foreach (var (fIdx, funcId) in funcsToRender)
            {
                string funcString = ModuleRenderer.RenderFunctionWat(
                    module, fIdx, "", true);
                using var outputFileStream = new FileStream(
                    $"{funcId}.part.wat", FileMode.Create);
                using var outputStreamWriter = new StreamWriter(outputFileStream);
                outputStreamWriter.Write(funcString);
                outputStreamWriter.Close();
            }
        }
    }
}
