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

namespace WitHarnessSpike.AggregateInnerParams.Generated.Validate
{
    /// <summary>
    /// Exercises option/tuple where the inner type is an aggregate
    /// (list / record). option&lt;list&gt;, option&lt;record&gt;,
    /// tuple&lt;u32, list&gt;.
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
                    Namespace = "WitHarnessSpike.AggregateInnerParams.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.AggregateInnerParams.Generated.BatcherHarness")!;
                var lineType = asm.GetType("WitHarnessSpike.AggregateInnerParams.Generated.Line")!;

                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // option<list<u32>>
                var sumOrDefault = harnessType.GetMethod("SumOrDefault", BindingFlags.Public | BindingFlags.Instance)!;
                var someSum = (uint)sumOrDefault.Invoke(harness, new object?[] {
                    new uint[] { 1, 2, 3, 4 }, (uint)99 })!;
                var noneSum = (uint)sumOrDefault.Invoke(harness, new object?[] { null, (uint)7 })!;
                Console.WriteLine($"sum-or-default(Some([1,2,3,4]), 99) = {someSum}");
                Console.WriteLine($"sum-or-default(None, 7) = {noneSum}");
                if (someSum != 10 || noneSum != 7)
                {
                    Console.Error.WriteLine($"FAIL: option<list> mismatch");
                    return 1;
                }

                // option<record>
                var formatLine = harnessType.GetMethod("FormatLine", BindingFlags.Public | BindingFlags.Instance)!;
                var line = Activator.CreateInstance(lineType, "alpha", (uint)42)!;
                var withLine = (string)formatLine.Invoke(harness, new object?[] { line })!;
                var withoutLine = (string)formatLine.Invoke(harness, new object?[] { null })!;
                Console.WriteLine($"format-line(Some(alpha@42)) = '{withLine}'");
                Console.WriteLine($"format-line(None) = '{withoutLine}'");
                if (withLine != "alpha@42" || withoutLine != "(none)")
                {
                    Console.Error.WriteLine($"FAIL: option<record> mismatch");
                    return 1;
                }

                // tuple<u32, list<u32>>
                var weighted = harnessType.GetMethod("WeightedSum", BindingFlags.Public | BindingFlags.Instance)!;
                var ws = ((uint)3, new uint[] { 4, 5, 6 });
                var weightedResult = (uint)weighted.Invoke(harness, new object?[] { ws })!;
                Console.WriteLine($"weighted-sum((3, [4,5,6])) = {weightedResult}");
                if (weightedResult != 45)
                {
                    Console.Error.WriteLine($"FAIL: tuple<u32, list> mismatch (expected 45, got {weightedResult})");
                    return 1;
                }

                Console.WriteLine("PASS — option/tuple of aggregate-inner direct params lower round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "batcher.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "batcher.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"batcher.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Batcher harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Batcher harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
