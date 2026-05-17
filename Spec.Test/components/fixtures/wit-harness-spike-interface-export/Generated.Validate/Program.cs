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

namespace WitHarnessSpike.InterfaceExport.Generated.Validate
{
    /// <summary>
    /// First fixture exercising an interface export. The world
    /// exports `ops` interface (add + swap) plus a free function
    /// `bake`. Interface-export methods flatten onto the harness
    /// with a "WacsInterfaceExportSpikeOps_" prefix; the wasm-side
    /// lookup uses "wacs:interface-export-spike/ops#&lt;fn&gt;".
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
                    Namespace = "WitHarnessSpike.InterfaceExport.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.InterfaceExport.Generated.CalculatorHarness")!;
                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // Free function (world-level)
                var bake = harnessType.GetMethod("Bake", BindingFlags.Public | BindingFlags.Instance)!;
                var bakeResult = (uint)bake.Invoke(harness, Array.Empty<object?>())!;
                Console.WriteLine($"bake() = {bakeResult}");
                if (bakeResult != 77)
                {
                    Console.Error.WriteLine($"FAIL: bake expected 77, got {bakeResult}");
                    return 1;
                }

                // Interface export — flat method with prefix
                var add = harnessType.GetMethod("WacsInterfaceExportSpikeOps_Add",
                    BindingFlags.Public | BindingFlags.Instance);
                if (add == null)
                {
                    Console.Error.WriteLine("FAIL: interface method WacsInterfaceExportSpikeOps_Add not found");
                    var methods = harnessType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    Console.Error.WriteLine("Available methods:");
                    foreach (var m in methods) Console.Error.WriteLine($"  {m.Name}");
                    return 1;
                }
                var addResult = (uint)add.Invoke(harness, new object?[] { (uint)7, (uint)35 })!;
                Console.WriteLine($"ops.add(7, 35) = {addResult}");
                if (addResult != 42)
                {
                    Console.Error.WriteLine($"FAIL: ops.add expected 42, got {addResult}");
                    return 1;
                }

                // Interface export with tuple return
                var swap = harnessType.GetMethod("WacsInterfaceExportSpikeOps_Swap",
                    BindingFlags.Public | BindingFlags.Instance)!;
                var swapResult = ((uint, uint))swap.Invoke(harness, new object?[] { (uint)1, (uint)2 })!;
                Console.WriteLine($"ops.swap(1, 2) = ({swapResult.Item1}, {swapResult.Item2})");
                if (swapResult.Item1 != 2 || swapResult.Item2 != 1)
                {
                    Console.Error.WriteLine($"FAIL: ops.swap expected (2,1), got ({swapResult.Item1},{swapResult.Item2})");
                    return 1;
                }

                Console.WriteLine("PASS — function-only interface export round-trip green.");
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
            throw new DirectoryNotFoundException(
                $"wit directory not found (looked at {sideBySide} and {devTree})");
        }

        private static string ResolveComponentPath()
        {
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "calculator.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "calculator.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"calculator.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Calculator harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Calculator harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
