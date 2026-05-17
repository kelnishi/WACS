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

namespace WitHarnessSpike.ListRecordParam.Generated.Validate
{
    /// <summary>
    /// Exercises <c>list&lt;record&gt;</c> as a direct param.
    /// Each element's canonical layout is written into a
    /// realloc'd block in wasm memory; strings inside the record
    /// recursively lower via LowerUtf8.
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
                    Namespace = "WitHarnessSpike.ListRecordParam.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.ListRecordParam.Generated.InventoryHarness")!;
                var itemType = asm.GetType("WitHarnessSpike.ListRecordParam.Generated.Item")!;

                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // Build [Item("apple", 3), Item("widget", 5)]
                var items = Array.CreateInstance(itemType, 2);
                items.SetValue(Activator.CreateInstance(itemType, "apple", (uint)3), 0);
                items.SetValue(Activator.CreateInstance(itemType, "widget", (uint)5), 1);

                var value = harnessType.GetMethod("InventoryValue", BindingFlags.Public | BindingFlags.Instance)!;
                var total = (uint)value.Invoke(harness, new object?[] { items, (uint)10 })!;
                Console.WriteLine($"inventory-value([apple:3, widget:5], 10) = {total}");
                if (total != 80) // (3+5) * 10 = 80
                {
                    Console.Error.WriteLine($"FAIL: expected 80, got {total}");
                    return 1;
                }

                var summary = harnessType.GetMethod("InventorySummary", BindingFlags.Public | BindingFlags.Instance)!;
                var sum = (string)summary.Invoke(harness, new object?[] { items })!;
                Console.WriteLine($"inventory-summary([apple:3, widget:5]) = '{sum}'");
                if (sum != "apple:3,widget:5")
                {
                    Console.Error.WriteLine($"FAIL: expected 'apple:3,widget:5', got '{sum}'");
                    return 1;
                }

                // Empty list path
                var empty = Array.CreateInstance(itemType, 0);
                var emptySum = (string)summary.Invoke(harness, new object?[] { empty })!;
                Console.WriteLine($"inventory-summary([]) = '{emptySum}'");
                if (emptySum != "")
                {
                    Console.Error.WriteLine($"FAIL: expected empty, got '{emptySum}'");
                    return 1;
                }

                Console.WriteLine("PASS — list<record> direct param lower round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "inventory.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "inventory.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"inventory.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Inventory harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Inventory harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
