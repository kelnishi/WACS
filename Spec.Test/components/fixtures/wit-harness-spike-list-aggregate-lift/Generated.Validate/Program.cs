// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Harness.Lib;
using Wacs.Core.Runtime;

namespace WitHarnessSpike.ListAggregateLift.Generated.Validate
{
    /// <summary>
    /// Lift-symmetry test: list&lt;option&gt;, list&lt;tuple&gt;,
    /// list&lt;enum&gt; round-trip as direct returns. Previously
    /// only list&lt;prim/string/record/variant&gt; lifted; now all
    /// six element kinds work.
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
                    Namespace = "WitHarnessSpike.ListAggregateLift.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.ListAggregateLift.Generated.CollectorHarness")!;
                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // list<option<u32>>
                var sparse = harnessType.GetMethod("SparseValues", BindingFlags.Public | BindingFlags.Instance)!;
                var sparseResult = (uint?[])sparse.Invoke(harness, Array.Empty<object?>())!;
                Console.WriteLine($"sparse-values() = [{string.Join(", ", sparseResult.Select(v => v?.ToString() ?? "None"))}]");
                var expected = new uint?[] { 10, null, 30, null, 50 };
                if (!sparseResult.SequenceEqual(expected))
                {
                    Console.Error.WriteLine($"FAIL: sparse-values mismatch");
                    return 1;
                }

                // list<tuple<u32, string>>
                var pairs = harnessType.GetMethod("Pairs", BindingFlags.Public | BindingFlags.Instance)!;
                var pairsResult = ((uint, string)[])pairs.Invoke(harness, Array.Empty<object?>())!;
                Console.WriteLine($"pairs() = [{string.Join(", ", pairsResult.Select(p => $"({p.Item1},{p.Item2})"))}]");
                var expectedPairs = new (uint, string)[] { (1, "one"), (2, "two"), (3, "three") };
                if (!pairsResult.SequenceEqual(expectedPairs))
                {
                    Console.Error.WriteLine($"FAIL: pairs mismatch");
                    return 1;
                }

                // list<enum>
                var signals = harnessType.GetMethod("Signals", BindingFlags.Public | BindingFlags.Instance)!;
                var signalsResult = (Array)signals.Invoke(harness, Array.Empty<object?>())!;
                var signalStrs = new string[signalsResult.Length];
                for (int i = 0; i < signalsResult.Length; i++)
                    signalStrs[i] = signalsResult.GetValue(i)!.ToString()!;
                Console.WriteLine($"signals() = [{string.Join(", ", signalStrs)}]");
                var expectedSignals = new[] { "Off", "High", "Low", "High" };
                if (!signalStrs.SequenceEqual(expectedSignals))
                {
                    Console.Error.WriteLine($"FAIL: signals mismatch");
                    return 1;
                }

                Console.WriteLine("PASS — list<option> / list<tuple> / list<enum> lift round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "collector.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "collector.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException("collector.component.wasm not found");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Collector harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Collector harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
