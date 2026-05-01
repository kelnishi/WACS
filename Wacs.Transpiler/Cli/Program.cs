// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandLine;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Wacs.Transpiler.Hosting;
using Wacs.WASIp1;

namespace Wacs.Transpiler.Cli
{
    /// <summary>
    /// `wasm-transpile` CLI: reads a .wasm module, runs the AOT transpiler,
    /// and writes a standalone .NET assembly to disk. See
    /// <see cref="CliOptions"/> for the flag surface.
    /// </summary>
    public static class Program
    {
        // Exit codes
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitTranspileFailure = 2;
        private const int ExitEmitMainConstraint = 3;
        private const int ExitRunFailure = 4;

        public static int Main(string[] args)
        {
            // Deprecation banner: wasm-transpile is superseded by the
            // unified `wacs` CLI (WACS.Cli). The legacy tool keeps
            // working unchanged so existing pipelines don't break,
            // but every invocation surfaces the migration path on
            // stderr (so it doesn't pollute --help / --version /
            // captured-stdout flows).
            Console.Error.WriteLine(
                "[deprecation] wasm-transpile is deprecated and will not "
                + "receive new features.");
            Console.Error.WriteLine(
                "[deprecation] Migrate to: dotnet tool install -g WACS.Cli "
                + "&& wacs run/build/inspect");

            int exit = ExitOk;
            Parser.Default.ParseArguments<CliOptions>(args)
                .WithParsed(opts => exit = Run(opts))
                .WithNotParsed(_ => exit = ExitUsage);
            return exit;
        }

        private static int Run(CliOptions opts)
        {
            var inputs = opts.Inputs
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
            if (inputs.Count == 0)
            {
                Console.Error.WriteLine("error: at least one --input file required");
                return ExitUsage;
            }
            foreach (var i in inputs)
                if (!File.Exists(i))
                {
                    Console.Error.WriteLine($"error: input file not found: {i}");
                    return ExitUsage;
                }
            var primaryInput = inputs[inputs.Count - 1];   // last input drives the export entry-point
            var output = Path.GetFullPath(opts.Output);

            bool hasHostBindings = opts.Wasi || opts.Bind.Any();
            bool hasComponentHostBindings = opts.Wasip2 || opts.HostPackage.Any();
            bool isInterpreterEngine = string.Equals(opts.Engine,
                "interpreter", StringComparison.OrdinalIgnoreCase);
            if (opts.Run && !opts.EmitMain && !hasHostBindings
                && !hasComponentHostBindings && !isInterpreterEngine)
            {
                Console.Error.WriteLine(
                    "error: --run requires --emit-main, --wasi, --bind, --wasip2, "
                    + "--host-package, or --engine interpreter");
                return ExitUsage;
            }
            if (isInterpreterEngine && (opts.Wasip2 || opts.HostPackage.Any()))
            {
                Console.Error.WriteLine(
                    "error: --engine interpreter doesn't yet thread --wasip2 / "
                    + "--host-package; use the default transpiler engine for those.");
                return ExitUsage;
            }

            TranspilerOptions options;
            try
            {
                options = BuildTranspilerOptions(opts);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return ExitUsage;
            }

            if (opts.Verbose)
            {
                Console.WriteLine($"input         {string.Join(", ", inputs.Select(Path.GetFileName))}");
                Console.WriteLine($"output        {output}");
                Console.WriteLine($"namespace     {opts.Namespace}");
                Console.WriteLine($"module        {opts.ModuleName}");
                Console.WriteLine($"simd          {options.Simd}");
                Console.WriteLine($"tail-calls    {options.EmitTailCallPrefix}");
                Console.WriteLine($"max-fn-size   {options.MaxFunctionSize}");
                Console.WriteLine($"data-storage  {options.DataStorage}");
                Console.WriteLine($"gc-checking   {options.GcTypeChecking}");
                if (opts.EmitMain)
                    Console.WriteLine($"emit-main     {opts.MainClass}.Main → {opts.EntryPoint}");
            }

            var timer = Stopwatch.StartNew();

            // Component-mode detection: peek at the first 8 bytes.
            // Component binaries carry layer=0x0001; core modules have
            // layer=0x0000. ComponentBinaryParser.IsComponentHeader is
            // the canonical discriminator.
            bool isComponent;
            try
            {
                isComponent = DetectComponent(primaryInput);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to read input header: {ex.Message}");
                return ExitTranspileFailure;
            }

            if (inputs.Count > 1 && isComponent)
            {
                Console.Error.WriteLine(
                    "error: multi-input mode currently supports core .wasm modules only; "
                    + "for cross-component composition use --host-package against a "
                    + "previously transpiled .dll.");
                return ExitUsage;
            }

            if (isComponent)
                return RunComponent(opts, primaryInput, output, options, timer);

            // Multi-input core-WASM path: parse each, register with a
            // shared WasmRuntime so cross-module imports resolve via
            // the interpreter's binding system, then either transpile
            // each (and save N .dlls) or run via the interpreter.
            if (inputs.Count > 1 || isInterpreterEngine)
                return RunMultiOrInterpreter(opts, inputs, output, options,
                    isInterpreterEngine, timer);

            var input = primaryInput;

            Module module;
            WasmRuntime runtime;
            ModuleInstance moduleInst;
            var hostBindings = new List<IBindable>();
            var disposables = new List<IDisposable>();
            try
            {
                using var fileStream = new FileStream(input, FileMode.Open, FileAccess.Read);
                module = BinaryModuleParser.ParseWasm(fileStream);
                runtime = new WasmRuntime();

                // --wasi is a shortcut that reuses the --bind machinery with
                // a curated WASI argv. Otherwise, load the assemblies named
                // by --bind, activate every IBindable with a parameterless
                // ctor, and hand them to the runtime.
                if (opts.Wasi)
                {
                    // Use the CLI-derived argv (wasm filename as argv[0],
                    // then positional --run trailing args) instead of the
                    // process-wide GetCommandLineArgs() that
                    // Wasi.DefaultConfiguration() picks up.
                    var wasiCfg = Wasi.DefaultConfiguration();
                    wasiCfg.Arguments = new List<string> { Path.GetFileName(input) };
                    wasiCfg.Arguments.AddRange(opts.Args);
                    var wasiBinding = new Wasi(wasiCfg);
                    hostBindings.Add(wasiBinding);
                    disposables.Add(wasiBinding);
                }

                foreach (var asmPath in opts.Bind)
                {
                    var loaded = BindingLoader.LoadFromAssembly(asmPath);
                    foreach (var b in loaded)
                    {
                        hostBindings.Add(b);
                        if (b is IDisposable d) disposables.Add(d);
                    }
                    if (opts.Verbose)
                        Console.WriteLine($"bind          {asmPath} → {loaded.Count} binding(s)");
                }

                foreach (var b in hostBindings)
                    b.BindToRuntime(runtime);

                moduleInst = runtime.InstantiateModule(module);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to parse/instantiate module: {ex.Message}");
                foreach (var d in disposables) d.Dispose();
                return ExitTranspileFailure;
            }

            TranspilationResult result;
            try
            {
                var transpiler = new ModuleTranspiler(opts.Namespace, options);
                result = transpiler.Transpile(moduleInst, runtime, opts.ModuleName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: transpilation failed: {ex.Message}");
                return ExitTranspileFailure;
            }

            Type? programType = null;
            if (opts.EmitMain)
            {
                try
                {
                    programType = MainEntryEmitter.Emit(result, opts.MainClass, opts.EntryPoint);
                }
                catch (MainEntryEmitter.ConstraintException ex)
                {
                    Console.Error.WriteLine($"error: --emit-main: {ex.Message}");
                    return ExitEmitMainConstraint;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: --emit-main failed: {ex.Message}");
                    return ExitTranspileFailure;
                }
            }

            // Invoke the in-process Main before SaveAssembly if --run is set;
            // SaveAssembly runs Lokad.ILPack over the dynamic module, which can
            // interfere with reflective dispatch on the live types.
            int runExit = ExitOk;
            if (opts.Run)
            {
                if (hasHostBindings)
                    runExit = HostedRunner.Run(result, runtime, moduleInst, opts.EntryPoint, opts.Verbose);
                else
                    runExit = InvokeEmittedMain(programType!, opts);
            }

            try
            {
                var outDir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);
                result.SaveAssembly(output);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to write output: {ex.Message}");
                return ExitTranspileFailure;
            }

            timer.Stop();

            if (opts.Verbose)
            {
                Console.WriteLine();
                Console.WriteLine($"transpiled {result.TranspiledCount} functions" +
                    (result.FallbackCount > 0 ? $" ({result.FallbackCount} fallback)" : "") +
                    $" in {timer.ElapsedMilliseconds}ms");
                if (result.Diagnostics.Count > 0)
                {
                    Console.WriteLine($"{result.Diagnostics.Count} diagnostic(s):");
                    foreach (var d in result.Diagnostics)
                        Console.WriteLine($"  {d}");
                }
            }
            else
            {
                Console.WriteLine($"wrote {output} ({result.TranspiledCount} functions, {timer.ElapsedMilliseconds}ms)");
            }

            foreach (var d in disposables) d.Dispose();
            return runExit;
        }

        // Multi-module / interpreter-engine path. Mirrors the
        // ModuleLinker pattern AotSpecTests uses for spec multi-
        // module fixtures: one shared WasmRuntime, each module
        // registered under its file basename so cross-module
        // imports resolve through the runtime's binding table.
        // For --engine interpreter, runs through the interpreter
        // and skips the transpile / save step; otherwise transpiles
        // each module to its own sibling .dll (the linker wires
        // them up at load time via the same name keys).
        private static int RunMultiOrInterpreter(CliOptions opts,
            List<string> inputs, string output, TranspilerOptions options,
            bool isInterpreterEngine, Stopwatch timer)
        {
            if (opts.Verbose)
            {
                Console.WriteLine($"engine        {(isInterpreterEngine ? "interpreter" : "transpiler")}");
                Console.WriteLine($"modules       {inputs.Count}");
            }

            var runtime = new WasmRuntime();
            var hostBindings = new List<IBindable>();
            var disposables = new List<IDisposable>();

            // --wasi / --bind apply to the SHARED runtime — any
            // module's host imports satisfied by the same set.
            if (opts.Wasi)
            {
                var wasiCfg = Wasi.DefaultConfiguration();
                wasiCfg.Arguments = new List<string>
                    { Path.GetFileName(inputs[inputs.Count - 1]) };
                wasiCfg.Arguments.AddRange(opts.Args);
                var wasiBinding = new Wasi(wasiCfg);
                hostBindings.Add(wasiBinding);
                disposables.Add(wasiBinding);
            }
            foreach (var asmPath in opts.Bind)
            {
                var loaded = BindingLoader.LoadFromAssembly(asmPath);
                foreach (var b in loaded)
                {
                    hostBindings.Add(b);
                    if (b is IDisposable d) disposables.Add(d);
                }
            }
            foreach (var b in hostBindings) b.BindToRuntime(runtime);

            // Parse + instantiate each module in input order. The
            // runtime resolves cross-module imports as each module's
            // exports become discoverable under its registered name.
            var parsed = new List<(string Name, Module M, ModuleInstance Inst)>();
            foreach (var inputPath in inputs)
            {
                var name = Path.GetFileNameWithoutExtension(inputPath);
                Module m;
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
                    Console.Error.WriteLine(
                        $"error: failed to parse/instantiate '{name}': {ex.Message}");
                    foreach (var d in disposables) d.Dispose();
                    return ExitTranspileFailure;
                }
                parsed.Add((name, m, inst));
                if (opts.Verbose)
                    Console.WriteLine($"registered    {name}");
            }

            // --engine interpreter: skip transpile + save. Run the
            // chosen export on the LAST module via the interpreter's
            // own dispatch.
            if (isInterpreterEngine)
            {
                int exit = ExitOk;
                if (opts.Run)
                {
                    var last = parsed[parsed.Count - 1];
                    try
                    {
                        if (!runtime.TryGetExportedFunction(
                                (last.Name, opts.EntryPoint), out var addr))
                        {
                            Console.Error.WriteLine(
                                $"error: export '{opts.EntryPoint}' not found on '{last.Name}'");
                            exit = ExitRunFailure;
                        }
                        else
                        {
                            var invoker = runtime.CreateStackInvoker(addr);
                            // Parse trailing args as scalar Values per
                            // the function type. v0 supports primitives
                            // only — argv is positional.
                            var ftype = runtime.GetFunctionType(addr);
                            var args = ParseInterpreterArgs(opts.Args.ToArray(),
                                ftype.ParameterTypes.Types);
                            var results = invoker(args);
                            if (results.Length == 1)
                                Console.WriteLine(FormatValue(results[0]));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"error: interpreter run failed: {ex.Message}");
                        exit = ExitRunFailure;
                    }
                }
                timer.Stop();
                if (opts.Verbose)
                    Console.WriteLine($"interpreter run completed in {timer.ElapsedMilliseconds}ms");
                foreach (var d in disposables) d.Dispose();
                return exit;
            }

            // Transpiler path: transpile each module separately and
            // save N .dlls. Output naming: -o is taken as the path for
            // the LAST module; siblings land at <basename>.dll in the
            // same directory. Cross-module wiring lives in the runtime
            // binding table the loader needs to reconstruct (see
            // TranspiledModuleLoader for the planned chain-mode
            // helper; today the .dlls are usable individually).
            var outputDir = Path.GetDirectoryName(output) ?? ".";
            var outputs = new List<string>();
            int totalTranspiled = 0;
            for (int i = 0; i < parsed.Count; i++)
            {
                var (name, _, inst) = parsed[i];
                var perOutput = i == parsed.Count - 1
                    ? output
                    : Path.Combine(outputDir, name + ".dll");

                TranspilationResult result;
                try
                {
                    var transpiler = new ModuleTranspiler(opts.Namespace, options);
                    result = transpiler.Transpile(inst, runtime, name);
                    totalTranspiled += result.TranspiledCount;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"error: transpilation of '{name}' failed: {ex.Message}");
                    foreach (var d in disposables) d.Dispose();
                    return ExitTranspileFailure;
                }

                try
                {
                    if (!Directory.Exists(outputDir))
                        Directory.CreateDirectory(outputDir);
                    result.SaveAssembly(perOutput);
                    outputs.Add(perOutput);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"error: saving '{name}' to {perOutput} failed: {ex.Message}");
                    foreach (var d in disposables) d.Dispose();
                    return ExitTranspileFailure;
                }
            }

            timer.Stop();
            if (opts.Verbose)
            {
                foreach (var p in outputs) Console.WriteLine($"wrote         {p}");
                Console.WriteLine($"transpiled {totalTranspiled} functions in {timer.ElapsedMilliseconds}ms");
            }
            else
            {
                Console.WriteLine(
                    $"wrote {outputs.Count} dll(s) to {outputDir} ({totalTranspiled} functions, {timer.ElapsedMilliseconds}ms)");
            }

            foreach (var d in disposables) d.Dispose();
            return ExitOk;
        }

        // Parse trailing CLI args as Values for the interpreter
        // run path. Only primitive scalars (i32/i64/f32/f64). Other
        // shapes fall through unparsed; those exports surface as a
        // run-time validation failure.
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

        // Read the first 8 bytes and ask ComponentBinaryParser which
        // shape it is. Files smaller than 8 bytes can't be either —
        // surface that as a non-component to let the core parser
        // produce its own clearer error.
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

        // Component-mode routing: parse + transpile via
        // ComponentTranspiler, threading --wasip2 / --host-package
        // host packages, then optionally emit Main + run + save.
        private static int RunComponent(CliOptions opts, string input,
            string output, TranspilerOptions options, Stopwatch timer)
        {
            if (opts.Verbose)
                Console.WriteLine("mode          component");

            // --bind / --wasi are core-WASI helpers that don't apply
            // to components — flag them as a usage error so users
            // don't expect them to wire WASI Preview 2 (which goes
            // through --wasip2 / --host-package instead).
            if (opts.Wasi || opts.Bind.Any())
            {
                Console.Error.WriteLine(
                    "error: --wasi / --bind do not apply to component binaries; " +
                    "use --wasip2 or --host-package <name> instead.");
                return ExitUsage;
            }

            TranspilationResult result;
            try
            {
                using var fs = new FileStream(input, FileMode.Open,
                    FileAccess.Read);
                result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: opts.Namespace,
                    moduleName: opts.ModuleName,
                    options: options,
                    configureImports: rt =>
                    {
                        // The runtime's instantiate-time validation
                        // wants a binding for every function import.
                        // Direct-linked imports never call through —
                        // the IL bypasses the table — so a throwing
                        // stub is the right shape: absence is silent
                        // success, presence-then-invocation surfaces
                        // a real bug. For multi-core components,
                        // stub the primary user module's imports
                        // (the same heuristic TranspileSingleModule
                        // uses to pick which core to transpile).
                        using var fs2 = new FileStream(input,
                            FileMode.Open, FileAccess.Read);
                        var parsed = ComponentTranspiler.Parse(fs2);
                        if (parsed.CoreModules.Count == 0) return;
                        int primary = parsed.CoreModules.Count == 1
                            ? 0
                            : (Wacs.ComponentModel.Runtime.ComponentInstance
                                .FindPrimaryCoreModuleIdx(parsed.Component)
                              ?? 0);
                        ComponentImportStubs.RegisterAll(rt,
                            parsed.CoreModules[primary]);
                    });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: component transpilation failed: {ex.Message}");
                return ExitTranspileFailure;
            }

            Type? programType = null;
            if (opts.EmitMain)
            {
                try
                {
                    programType = ComponentMainEntryEmitter.Emit(
                        result, opts.MainClass, opts.EntryPoint,
                        options.HostPackages);
                }
                catch (MainEntryEmitter.ConstraintException ex)
                {
                    Console.Error.WriteLine($"error: --emit-main: {ex.Message}");
                    return ExitEmitMainConstraint;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: --emit-main failed: {ex.Message}");
                    return ExitTranspileFailure;
                }
            }

            int runExit = ExitOk;
            if (opts.Run)
            {
                if (!opts.EmitMain)
                {
                    Console.Error.WriteLine(
                        "error: --run on component binaries requires --emit-main.");
                    return ExitUsage;
                }
                runExit = InvokeEmittedMain(programType!, opts);
            }

            try
            {
                var outDir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);
                result.SaveAssembly(output);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to write output: {ex.Message}");
                return ExitTranspileFailure;
            }

            timer.Stop();

            if (opts.Verbose)
            {
                Console.WriteLine();
                Console.WriteLine($"transpiled {result.TranspiledCount} functions" +
                    (result.FallbackCount > 0 ? $" ({result.FallbackCount} fallback)" : "") +
                    $" in {timer.ElapsedMilliseconds}ms");
                if (result.Diagnostics.Count > 0)
                {
                    Console.WriteLine($"{result.Diagnostics.Count} diagnostic(s):");
                    foreach (var d in result.Diagnostics)
                        Console.WriteLine($"  {d}");
                }
            }
            else
            {
                Console.WriteLine($"wrote {output} ({result.TranspiledCount} functions, {timer.ElapsedMilliseconds}ms)");
            }

            return runExit;
        }

        private static int InvokeEmittedMain(Type programType, CliOptions opts)
        {
            var main = programType.GetMethod("Main",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (main == null)
            {
                Console.Error.WriteLine($"error: --run: no static Main(string[]) on '{programType.FullName}'");
                return ExitRunFailure;
            }
            var forwarded = System.Linq.Enumerable.ToArray(opts.Args);
            if (opts.Verbose)
                Console.WriteLine($"run           {programType.FullName}.Main({string.Join(" ", forwarded)})");
            try
            {
                var rc = main.Invoke(null, new object?[] { forwarded });
                return rc is int i ? i : ExitOk;
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                Console.Error.WriteLine($"error: --run: {tie.InnerException?.Message ?? tie.Message}");
                return ExitRunFailure;
            }
        }

        private static TranspilerOptions BuildTranspilerOptions(CliOptions opts)
        {
            var t = new TranspilerOptions
            {
                Simd = ParseSimd(opts.Simd),
                EmitTailCallPrefix = !opts.NoTailCalls,
                MaxFunctionSize = opts.MaxFunctionSize,
                DataStorage = ParseDataStorage(opts.DataStorage),
                GcTypeChecking = ParseGcChecking(opts.GcChecking),
                HostPackages = ResolveHostPackages(opts),
            };
            return t;
        }

        // --wasip2 expands to "Wacs.WASI.Preview2"; --host-package
        // accepts either a simple assembly name (Assembly.Load) or a
        // file path (Assembly.LoadFrom). Failures abort the CLI with
        // a usage error so the user gets a deterministic message
        // before transpilation begins.
        private static IReadOnlyList<System.Reflection.Assembly> ResolveHostPackages(CliOptions opts)
        {
            var names = new List<string>();
            if (opts.Wasip2) names.Add("Wacs.WASI.Preview2");
            foreach (var n in opts.HostPackage)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                names.Add(n.Trim());
            }
            if (names.Count == 0) return System.Array.Empty<System.Reflection.Assembly>();

            var asms = new List<System.Reflection.Assembly>(names.Count);
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
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

        private static SimdStrategy ParseSimd(string s) => s.ToLowerInvariant() switch
        {
            "interpreter" => SimdStrategy.InterpreterDispatch,
            "scalar" => SimdStrategy.ScalarReference,
            "intrinsics" => SimdStrategy.HardwareIntrinsics,
            _ => throw new ArgumentException(
                $"unknown --simd value '{s}'; expected interpreter | scalar | intrinsics"),
        };

        private static DataSegmentStorage ParseDataStorage(string s) => s.ToLowerInvariant() switch
        {
            "compressed" => DataSegmentStorage.CompressedResource,
            "raw" => DataSegmentStorage.RawResource,
            "static" => DataSegmentStorage.StaticArrays,
            _ => throw new ArgumentException(
                $"unknown --data-storage value '{s}'; expected compressed | raw | static"),
        };

        private static TranspilerCapabilities ParseGcChecking(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return TranspilerCapabilities.None;
            var flags = TranspilerCapabilities.None;
            foreach (var piece in s.Split(','))
            {
                var name = piece.Trim();
                if (name.Length == 0) continue;
                if (!Enum.TryParse<TranspilerCapabilities>(name, ignoreCase: true, out var v))
                    throw new ArgumentException(
                        $"unknown --gc-checking flag '{name}'; expected comma-separated " +
                        $"{string.Join(" | ", Enum.GetNames(typeof(TranspilerCapabilities)))}");
                flags |= v;
            }
            return flags;
        }
    }
}
