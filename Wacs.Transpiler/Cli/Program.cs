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
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
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
            int exit = ExitOk;
            Parser.Default.ParseArguments<CliOptions>(args)
                .WithParsed(opts => exit = Run(opts))
                .WithNotParsed(_ => exit = ExitUsage);
            return exit;
        }

        private static int Run(CliOptions opts)
        {
            var input = Path.GetFullPath(opts.Input);
            var output = Path.GetFullPath(opts.Output);

            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"error: input file not found: {input}");
                return ExitUsage;
            }

            bool hasHostBindings = opts.Wasi || opts.Bind.Any();
            if (opts.Run && !opts.EmitMain && !hasHostBindings)
            {
                Console.Error.WriteLine("error: --run requires --emit-main, --wasi, or --bind");
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
                Console.WriteLine($"input         {input}");
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
            };
            return t;
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
