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
using System.Reflection;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.WASIp1;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Wacs.Transpiler.Hosting;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs build` verb handler. Transpiles one or more `.wasm`
    /// modules to .NET assemblies. Auto-routes between core-WASM
    /// and component-mode based on the input header byte; multi-
    /// input runs apply ModuleLinker composition with sibling .dll
    /// outputs.
    /// </summary>
    public static class BuildHandler
    {
        public static int Execute(BuildOptions opts)
        {
            var files = (opts.Files ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
            if (files.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs build requires at least one input file.");
                return 1;
            }
            foreach (var f in files)
            {
                if (!File.Exists(f))
                {
                    System.Console.Error.WriteLine(
                        "error: input file not found: " + f);
                    return 1;
                }
            }
            if (string.IsNullOrEmpty(opts.Output))
            {
                System.Console.Error.WriteLine(
                    "error: wacs build requires --output (-o) <path>.");
                return 1;
            }

            var output = Path.GetFullPath(opts.Output);
            var primaryInput = files[files.Count - 1];

            bool entryIsComponent;
            try
            {
                entryIsComponent = DetectComponent(primaryInput);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to read input header: " + ex.Message);
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

            var timer = Stopwatch.StartNew();

            if (entryIsComponent)
                return BuildComponent(opts, primaryInput, output, timer);

            if (files.Count > 1)
                return BuildMultiCore(opts, files, output, timer);

            return BuildSingleCore(opts, primaryInput, output, timer);
        }

        // ============================================================
        // Single-file core wasm
        // ============================================================

        private static int BuildSingleCore(BuildOptions opts,
            string input, string output, Stopwatch timer)
        {
            var runtime = new WasmRuntime();
            var disposables = new List<IDisposable>();
            ModuleInstance modInst;
            Wacs.Core.Module module;
            try
            {
                using var fs = new FileStream(input, FileMode.Open, FileAccess.Read);
                module = BinaryModuleParser.ParseWasm(fs);

                BindHostsToRuntime(opts, runtime, disposables, input);

                modInst = runtime.InstantiateModule(module);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to parse/instantiate module: " + ex.Message);
                foreach (var d in disposables) d.Dispose();
                return 1;
            }

            TranspilationResult result;
            try
            {
                var transpiler = new ModuleTranspiler(opts.Namespace,
                    BuildTranspilerOptions(opts));
                result = transpiler.Transpile(modInst, runtime, opts.ModuleName);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: transpilation failed: " + ex.Message);
                return 1;
            }

            if (opts.EmitMain)
            {
                try
                {
                    MainEntryEmitter.Emit(result, opts.MainClass, opts.EntryPoint);
                }
                catch (MainEntryEmitter.ConstraintException ex)
                {
                    System.Console.Error.WriteLine(
                        "error: --emit-main: " + ex.Message);
                    return 3;
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        "error: --emit-main failed: " + ex.Message);
                    return 1;
                }
            }

            if (!Save(result, output)) return 1;
            timer.Stop();
            ReportSingle(opts, output, result, timer);
            foreach (var d in disposables) d.Dispose();
            return 0;
        }

        // ============================================================
        // Multi-file core wasm
        // ============================================================

        private static int BuildMultiCore(BuildOptions opts,
            List<string> inputs, string output, Stopwatch timer)
        {
            if (opts.Verbose)
                System.Console.Error.WriteLine(
                    "modules       " + inputs.Count);

            var runtime = new WasmRuntime();
            var disposables = new List<IDisposable>();

            // --wasi / --bind apply to the SHARED runtime.
            try
            {
                BindHostsToRuntime(opts, runtime, disposables, inputs[inputs.Count - 1]);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: host binding failed: " + ex.Message);
                foreach (var d in disposables) d.Dispose();
                return 1;
            }

            var parsed = new List<(string Name, ModuleInstance Inst)>();
            foreach (var path in inputs)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    using var fs = new FileStream(path, FileMode.Open,
                        FileAccess.Read);
                    var m = BinaryModuleParser.ParseWasm(fs);
                    var inst = runtime.InstantiateModule(m);
                    runtime.RegisterModule(name, inst);
                    parsed.Add((name, inst));
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        "error: failed to parse/instantiate '" + name
                        + "': " + ex.Message);
                    foreach (var d in disposables) d.Dispose();
                    return 1;
                }
                if (opts.Verbose)
                    System.Console.Error.WriteLine(
                        "registered    " + name);
            }

            var outputDir = Path.GetDirectoryName(output) ?? ".";
            var outputs = new List<string>();
            int totalTranspiled = 0;
            for (int i = 0; i < parsed.Count; i++)
            {
                var (name, inst) = parsed[i];
                var perOutput = i == parsed.Count - 1
                    ? output
                    : Path.Combine(outputDir, name + ".dll");

                TranspilationResult result;
                try
                {
                    var transpiler = new ModuleTranspiler(opts.Namespace,
                        BuildTranspilerOptions(opts));
                    result = transpiler.Transpile(inst, runtime, name);
                    totalTranspiled += result.TranspiledCount;
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        "error: transpilation of '" + name + "' failed: "
                        + ex.Message);
                    foreach (var d in disposables) d.Dispose();
                    return 1;
                }

                if (!Save(result, perOutput))
                {
                    foreach (var d in disposables) d.Dispose();
                    return 1;
                }
                outputs.Add(perOutput);
            }

            timer.Stop();
            ReportMulti(opts, outputDir, outputs, totalTranspiled, timer);
            foreach (var d in disposables) d.Dispose();
            return 0;
        }

        // ============================================================
        // Component-mode
        // ============================================================

        private static int BuildComponent(BuildOptions opts,
            string input, string output, Stopwatch timer)
        {
            if (opts.Wasi || (opts.Bind != null && opts.Bind.Any()))
            {
                System.Console.Error.WriteLine(
                    "error: --wasi / --bind do not apply to component "
                    + "binaries; use --wasip2 or --host-package <name> instead.");
                return 1;
            }

            var hostPackages = ResolveHostPackages(opts);
            var tOpts = BuildTranspilerOptions(opts);
            tOpts.HostPackages = hostPackages;

            TranspilationResult result;
            try
            {
                using var fs = new FileStream(input, FileMode.Open,
                    FileAccess.Read);
                result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: opts.Namespace,
                    moduleName: opts.ModuleName,
                    options: tOpts,
                    configureImports: rt =>
                    {
                        using var fs2 = new FileStream(input, FileMode.Open,
                            FileAccess.Read);
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
                    "error: component transpilation failed: " + ex.Message);
                return 1;
            }

            if (opts.EmitMain)
            {
                try
                {
                    ComponentMainEntryEmitter.Emit(result,
                        opts.MainClass, opts.EntryPoint, hostPackages);
                }
                catch (MainEntryEmitter.ConstraintException ex)
                {
                    System.Console.Error.WriteLine(
                        "error: --emit-main: " + ex.Message);
                    return 3;
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        "error: --emit-main failed: " + ex.Message);
                    return 1;
                }
            }

            if (!Save(result, output)) return 1;
            timer.Stop();
            ReportSingle(opts, output, result, timer);
            return 0;
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

        private static void BindHostsToRuntime(BuildOptions opts,
            WasmRuntime runtime, List<IDisposable> disposables,
            string primaryInput)
        {
            if (opts.Wasi)
            {
                var wasiCfg = Wasi.DefaultConfiguration();
                wasiCfg.Arguments = new List<string>
                    { Path.GetFileName(primaryInput) };
                var wasiBinding = new Wacs.WASIp1.Wasi(wasiCfg);
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
                if (opts.Verbose)
                    System.Console.Error.WriteLine(
                        "bind          " + asmPath + " -> "
                        + loaded.Count + " binding(s)");
            }
        }

        private static IReadOnlyList<Assembly> ResolveHostPackages(BuildOptions opts)
        {
            var names = new List<string>();
            if (opts.Wasip2) names.Add("Wacs.WASI.Preview2");
            foreach (var n in opts.HostPackage ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                names.Add(n.Trim());
            }
            if (names.Count == 0) return Array.Empty<Assembly>();

            var asms = new List<Assembly>(names.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in names)
            {
                if (!seen.Add(n)) continue;
                Assembly asm;
                try
                {
                    asm = File.Exists(n)
                        ? Assembly.LoadFrom(Path.GetFullPath(n))
                        : Assembly.Load(n);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        "--host-package: failed to load '" + n + "': "
                        + ex.Message);
                }
                asms.Add(asm);
            }
            return asms;
        }

        private static TranspilerOptions BuildTranspilerOptions(BuildOptions opts)
        {
            return new TranspilerOptions
            {
                Simd = ParseSimd(opts.Simd),
                EmitTailCallPrefix = !opts.NoTailCalls,
                MaxFunctionSize = opts.MaxFnSize,
                DataStorage = ParseDataStorage(opts.DataStorage),
                GcTypeChecking = ParseGcChecking(opts.GcChecking),
            };
        }

        private static bool Save(TranspilationResult result, string output)
        {
            try
            {
                var outDir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);
                result.SaveAssembly(output);
                return true;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to write output: " + ex.Message);
                return false;
            }
        }

        private static void ReportSingle(BuildOptions opts, string output,
            TranspilationResult result, Stopwatch timer)
        {
            if (opts.Verbose)
            {
                System.Console.WriteLine();
                System.Console.WriteLine(
                    "transpiled " + result.TranspiledCount + " functions"
                    + (result.FallbackCount > 0
                        ? " (" + result.FallbackCount + " fallback)"
                        : "")
                    + " in " + timer.ElapsedMilliseconds + "ms");
                if (result.Diagnostics.Count > 0)
                {
                    System.Console.WriteLine(
                        result.Diagnostics.Count + " diagnostic(s):");
                    foreach (var d in result.Diagnostics)
                        System.Console.WriteLine("  " + d);
                }
            }
            else
            {
                System.Console.WriteLine(
                    "wrote " + output + " ("
                    + result.TranspiledCount + " functions, "
                    + timer.ElapsedMilliseconds + "ms)");
            }
        }

        private static void ReportMulti(BuildOptions opts, string outputDir,
            List<string> outputs, int totalTranspiled, Stopwatch timer)
        {
            if (opts.Verbose)
            {
                foreach (var p in outputs)
                    System.Console.WriteLine("wrote         " + p);
                System.Console.WriteLine(
                    "transpiled " + totalTranspiled + " functions in "
                    + timer.ElapsedMilliseconds + "ms");
            }
            else
            {
                System.Console.WriteLine(
                    "wrote " + outputs.Count + " dll(s) to " + outputDir
                    + " (" + totalTranspiled + " functions, "
                    + timer.ElapsedMilliseconds + "ms)");
            }
        }

        private static SimdStrategy ParseSimd(string s) => s.ToLowerInvariant() switch
        {
            "interpreter" => SimdStrategy.InterpreterDispatch,
            "scalar" => SimdStrategy.ScalarReference,
            "intrinsics" => SimdStrategy.HardwareIntrinsics,
            _ => throw new ArgumentException(
                "unknown --simd value '" + s
                + "'; expected interpreter | scalar | intrinsics"),
        };

        private static DataSegmentStorage ParseDataStorage(string s) =>
            s.ToLowerInvariant() switch
        {
            "compressed" => DataSegmentStorage.CompressedResource,
            "raw" => DataSegmentStorage.RawResource,
            "static" => DataSegmentStorage.StaticArrays,
            _ => throw new ArgumentException(
                "unknown --data-storage value '" + s
                + "'; expected compressed | raw | static"),
        };

        private static TranspilerCapabilities ParseGcChecking(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return TranspilerCapabilities.None;
            var flags = TranspilerCapabilities.None;
            foreach (var piece in s.Split(','))
            {
                var name = piece.Trim();
                if (name.Length == 0) continue;
                if (!Enum.TryParse<TranspilerCapabilities>(name, true, out var v))
                    throw new ArgumentException(
                        "unknown --gc-checking flag '" + name + "'");
                flags |= v;
            }
            return flags;
        }
    }
}
