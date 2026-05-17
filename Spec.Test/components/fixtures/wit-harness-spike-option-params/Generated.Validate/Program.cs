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

namespace WitHarnessSpike.OptionParams.Generated.Validate
{
    /// <summary>
    /// Exercises option&lt;T&gt; as a direct param: flat-lowers to
    /// (i32 disc, T_flat…). Value-type inner (u32) and reference
    /// inner (string).
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
                    Namespace = "WitHarnessSpike.OptionParams.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.OptionParams.Generated.GreeterHarness")!;
                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                var doubleOr = harnessType.GetMethod("DoubleOr", BindingFlags.Public | BindingFlags.Instance)!;
                // Some(21), fallback 999 -> 42
                var r1 = (uint)doubleOr.Invoke(harness, new object?[] { (uint?)21, (uint)999 })!;
                Console.WriteLine($"double-or(Some(21), 999) = {r1}");
                // None, fallback 7 -> 7
                var r2 = (uint)doubleOr.Invoke(harness, new object?[] { (uint?)null, (uint)7 })!;
                Console.WriteLine($"double-or(None, 7) = {r2}");
                if (r1 != 42 || r2 != 7)
                {
                    Console.Error.WriteLine($"FAIL: option<u32> param mismatch (got {r1}, {r2})");
                    return 1;
                }

                var greet = harnessType.GetMethod("Greet", BindingFlags.Public | BindingFlags.Instance)!;
                var g1 = (string)greet.Invoke(harness, new object?[] { "world" })!;
                Console.WriteLine($"greet(Some('world')) = '{g1}'");
                var g2 = (string)greet.Invoke(harness, new object?[] { null })!;
                Console.WriteLine($"greet(None) = '{g2}'");
                if (g1 != "hello, world!" || g2 != "hello, stranger!")
                {
                    Console.Error.WriteLine($"FAIL: option<string> param mismatch (got '{g1}', '{g2}')");
                    return 1;
                }

                Console.WriteLine("PASS — option<T> direct params lower round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "greeter.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "greeter.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"greeter.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Greeter harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Greeter harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
