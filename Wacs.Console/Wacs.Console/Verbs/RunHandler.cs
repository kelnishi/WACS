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
            string fileExtension = Path.GetExtension(wasmPath).ToLowerInvariant();
            if (fileExtension != ".wasm" && fileExtension != ".wat")
            {
                System.Console.Error.WriteLine(
                    $"Error: Invalid file extension: {fileExtension}. "
                    + "Expected .wasm or .wat (for .wast scripts use a spec runner).");
                return 1;
            }

            // Validate WASI directories.
            foreach (var dir in opts.Directories ?? Enumerable.Empty<string>())
            {
                if (!Directory.Exists(dir))
                {
                    System.Console.Error.WriteLine($"Error: Directory not found: {dir}");
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
            foreach (var asmPath in opts.Bind ?? Enumerable.Empty<string>())
            {
                var loaded = BindingLoader.LoadFromAssembly(asmPath);
                foreach (var b in loaded) b.BindToRuntime(runtime);
                if (opts.Verbose)
                    System.Console.Error.WriteLine(
                        "bind          " + asmPath + " -> "
                        + loaded.Count + " binding(s)");
            }

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
            foreach (var asmPath in opts.Bind ?? Enumerable.Empty<string>())
            {
                var loaded = BindingLoader.LoadFromAssembly(asmPath);
                foreach (var b in loaded)
                {
                    b.BindToRuntime(runtime);
                    if (b is IDisposable d) disposables.Add(d);
                }
            }

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
                    inst = runtime.InstantiateModule(m);
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
                // Interpreter component path. configureImports stays
                // empty here — components that need host imports
                // should use --wasip2 / --host-package, which routes
                // through the transpiler path above.
                var bytes = File.ReadAllBytes(componentPath);
                var ci = Wacs.ComponentModel.Runtime.ComponentInstance
                    .Instantiate(bytes, _ => { });

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
            string entry = !string.IsNullOrEmpty(opts.Call)
                ? opts.Call : "_start";
            try
            {
                return ComponentMainHost.Run(result.ModuleClass!,
                    (opts.Args ?? Enumerable.Empty<string>()).ToArray(),
                    entry);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    $"error: component run failed: {ex.Message}");
                return 1;
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
            if (opts.Wasip2) names.Add("Wacs.WASI.Preview2");
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
