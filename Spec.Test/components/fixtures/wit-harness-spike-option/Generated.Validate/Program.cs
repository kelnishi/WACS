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

namespace WitHarnessSpike.Option.Generated.Validate
{
    /// <summary>
    /// Exercises option&lt;T&gt; for both value types (u32 → Nullable&lt;uint&gt;)
    /// and reference types (string → string with null sentinel).
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
                    Namespace = "WitHarnessSpike.Option.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.Option.Generated.PickerHarness")!;
                var snapshotType = asm.GetType("WitHarnessSpike.Option.Generated.Snapshot")!;

                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                var pick = harnessType.GetMethod("Pick",
                    BindingFlags.Public | BindingFlags.Instance)!;

                // Both Some
                var both = pick.Invoke(harness, new object?[] { (uint)1, (uint)1 })!;
                var maybeNum1 = (uint?)snapshotType.GetProperty("MaybeNum")!.GetValue(both);
                var maybeName1 = (string?)snapshotType.GetProperty("MaybeName")!.GetValue(both);
                Console.WriteLine($"pick(1,1) = (maybe-num={(maybeNum1 == null ? "None" : maybeNum1.ToString())}, maybe-name={(maybeName1 == null ? "None" : "'" + maybeName1 + "'")})");
                if (maybeNum1 != 42 || maybeName1 != "hi")
                {
                    Console.Error.WriteLine($"FAIL: expected (42, 'hi'), got ({maybeNum1}, {maybeName1})");
                    return 1;
                }

                // Both None
                var neither = pick.Invoke(harness, new object?[] { (uint)0, (uint)0 })!;
                var maybeNum2 = (uint?)snapshotType.GetProperty("MaybeNum")!.GetValue(neither);
                var maybeName2 = (string?)snapshotType.GetProperty("MaybeName")!.GetValue(neither);
                Console.WriteLine($"pick(0,0) = (maybe-num={(maybeNum2 == null ? "None" : maybeNum2.ToString())}, maybe-name={(maybeName2 == null ? "None" : "'" + maybeName2 + "'")})");
                if (maybeNum2 != null || maybeName2 != null)
                {
                    Console.Error.WriteLine($"FAIL: expected (None, None), got ({maybeNum2}, {maybeName2})");
                    return 1;
                }

                // Mixed
                var mixed = pick.Invoke(harness, new object?[] { (uint)1, (uint)0 })!;
                var maybeNum3 = (uint?)snapshotType.GetProperty("MaybeNum")!.GetValue(mixed);
                var maybeName3 = (string?)snapshotType.GetProperty("MaybeName")!.GetValue(mixed);
                Console.WriteLine($"pick(1,0) = (maybe-num={(maybeNum3 == null ? "None" : maybeNum3.ToString())}, maybe-name={(maybeName3 == null ? "None" : "'" + maybeName3 + "'")})");
                if (maybeNum3 != 42 || maybeName3 != null)
                {
                    Console.Error.WriteLine($"FAIL: expected (42, None), got ({maybeNum3}, {maybeName3})");
                    return 1;
                }

                Console.WriteLine("PASS — option<u32> + option<string> lift round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "picker.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "picker.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"picker.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Picker harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Picker harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
