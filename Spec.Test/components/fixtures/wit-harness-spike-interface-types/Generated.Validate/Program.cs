// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using Wacs.ComponentModel.Harness.Lib;
using Wacs.Core.Runtime;

namespace WitHarnessSpike.InterfaceTypes.Generated.Validate
{
    /// <summary>
    /// Exercises interface-level type declarations (record, enum,
    /// variant). The geometry interface declares Point, Quadrant,
    /// Region — these emit into the interface's own C# sub-
    /// namespace, distinct from the world namespace.
    /// </summary>
    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var witDir = ResolveWitDirectory();
                var componentPath = ResolveComponentPath();
                Console.WriteLine($"wit: {witDir}");
                Console.WriteLine($"component: {componentPath}");

                var asm = HarnessEmitter.EmitInMemory(witDir, new HarnessOptions
                {
                    Namespace = "WitHarnessSpike.InterfaceTypes.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.InterfaceTypes.Generated.CartographerHarness")!;
                var ifaceNs = "WitHarnessSpike.InterfaceTypes.Generated.WacsInterfaceTypesSpikeGeometry";
                var pointType = asm.GetType(ifaceNs + ".Point")!;
                var quadrantType = asm.GetType(ifaceNs + ".Quadrant")!;
                var regionType = asm.GetType(ifaceNs + ".Region")!;
                var emptyCase = regionType.GetNestedType("Empty")!;
                var pointOnlyCase = regionType.GetNestedType("PointOnly")!;
                var labeledCase = regionType.GetNestedType("Labeled")!;

                Console.WriteLine($"Interface types live at: {ifaceNs}.*");
                Console.WriteLine($"  Point     = {pointType.FullName}");
                Console.WriteLine($"  Quadrant  = {quadrantType.FullName}");
                Console.WriteLine($"  Region    = {regionType.FullName}");

                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // classify((150, 200)) → Ne
                var classify = harnessType.GetMethod("WacsInterfaceTypesSpikeGeometry_Classify",
                    BindingFlags.Public | BindingFlags.Instance);
                if (classify == null)
                {
                    Console.Error.WriteLine("FAIL: classify method not found");
                    foreach (var m in harnessType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        Console.Error.WriteLine("  " + m.Name);
                    return 1;
                }
                var p = Activator.CreateInstance(pointType, (uint)150, (uint)200)!;
                var q = classify.Invoke(harness, new object?[] { p })!;
                Console.WriteLine($"classify((150,200)) = {q}");
                if (q.ToString() != "Ne")
                {
                    Console.Error.WriteLine($"FAIL: expected Ne, got {q}");
                    return 1;
                }

                // describe variants
                var describe = harnessType.GetMethod("WacsInterfaceTypesSpikeGeometry_Describe",
                    BindingFlags.Public | BindingFlags.Instance)!;

                var emptyInst = Activator.CreateInstance(emptyCase)!;
                var emptyResult = (string)describe.Invoke(harness, new object?[] { emptyInst })!;
                Console.WriteLine($"describe(Empty) = '{emptyResult}'");

                var pointInst = Activator.CreateInstance(pointType, (uint)3, (uint)4)!;
                var pointOnlyInst = Activator.CreateInstance(pointOnlyCase, pointInst)!;
                var pointOnlyResult = (string)describe.Invoke(harness, new object?[] { pointOnlyInst })!;
                Console.WriteLine($"describe(PointOnly(3,4)) = '{pointOnlyResult}'");

                var labeledInst = Activator.CreateInstance(labeledCase, "spot")!;
                var labeledResult = (string)describe.Invoke(harness, new object?[] { labeledInst })!;
                Console.WriteLine($"describe(Labeled('spot')) = '{labeledResult}'");

                if (emptyResult != "(empty)" || pointOnlyResult != "point(3,4)" || labeledResult != "label<spot>")
                {
                    Console.Error.WriteLine("FAIL: describe outputs mismatched");
                    return 1;
                }

                Console.WriteLine("PASS — interface-level types (record + enum + variant) round-trip green.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.Error.WriteLine($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        private static string ResolveWitDirectory()
        {
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "wit");
            if (Directory.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wit"));
            if (Directory.Exists(devTree)) return devTree;
            throw new DirectoryNotFoundException($"wit directory not found");
        }

        private static string ResolveComponentPath()
        {
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "cartographer.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "cartographer.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException("cartographer.component.wasm not found");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Cartographer harness does not implement WASI runtime.");
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/error@0.2.0", "[resource-drop]error"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[resource-drop]pollable"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]input-stream"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]output-stream"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-input@0.2.0", "[resource-drop]terminal-input"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-output@0.2.0", "[resource-drop]terminal-output"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/environment@0.2.0", "get-environment"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/exit@0.2.0", "exit"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[method]pollable.block"), drop);
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.check-write"), (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.blocking-flush"), (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.write"), (_, _, _, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Func<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.subscribe"), (_, _) => throw new NotSupportedException("stub"));
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Cartographer harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
