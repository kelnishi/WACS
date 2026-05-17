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

namespace WitHarnessSpike.LowerParams.Generated.Validate
{
    /// <summary>
    /// Exercises the generic lower path: strings and lists in PARAMS.
    /// Covers multi-string params, string param + primitive return,
    /// list&lt;u32&gt; param, and list&lt;string&gt; param (the most
    /// complex per-element lower — innerPtr/innerLen written into each
    /// slot).
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
                    Namespace = "WitHarnessSpike.LowerParams.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.LowerParams.Generated.RepeaterHarness")!;
                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // shout("hi", 3) -> "hi!hi!hi!"
                var shout = harnessType.GetMethod("Shout", BindingFlags.Public | BindingFlags.Instance)!;
                var shoutResult = (string)shout.Invoke(harness, new object?[] { "hi", (uint)3 })!;
                Console.WriteLine($"shout('hi', 3) = '{shoutResult}'");
                if (shoutResult != "hi!hi!hi!")
                {
                    Console.Error.WriteLine($"FAIL: expected 'hi!hi!hi!', got '{shoutResult}'");
                    return 1;
                }

                // length-of("hello world") -> 11
                var lengthOf = harnessType.GetMethod("LengthOf", BindingFlags.Public | BindingFlags.Instance)!;
                var lenResult = (uint)lengthOf.Invoke(harness, new object?[] { "hello world" })!;
                Console.WriteLine($"length-of('hello world') = {lenResult}");
                if (lenResult != 11)
                {
                    Console.Error.WriteLine($"FAIL: expected 11, got {lenResult}");
                    return 1;
                }

                // sum([1, 2, 3, 4, 5]) -> 15
                var sum = harnessType.GetMethod("Sum", BindingFlags.Public | BindingFlags.Instance)!;
                var sumResult = (uint)sum.Invoke(harness, new object?[] { new uint[] { 1, 2, 3, 4, 5 } })!;
                Console.WriteLine($"sum([1..5]) = {sumResult}");
                if (sumResult != 15)
                {
                    Console.Error.WriteLine($"FAIL: expected 15, got {sumResult}");
                    return 1;
                }

                // sum([]) -> 0  (exercises the zero-length lower path)
                var sumEmpty = (uint)sum.Invoke(harness, new object?[] { new uint[0] })!;
                Console.WriteLine($"sum([]) = {sumEmpty}");
                if (sumEmpty != 0)
                {
                    Console.Error.WriteLine($"FAIL: expected 0, got {sumEmpty}");
                    return 1;
                }

                // total-chars(["alpha", "beta", "gamma"]) -> 5+4+5 = 14
                var totalChars = harnessType.GetMethod("TotalChars", BindingFlags.Public | BindingFlags.Instance)!;
                var charsResult = (uint)totalChars.Invoke(harness, new object?[] {
                    new string[] { "alpha", "beta", "gamma" } })!;
                Console.WriteLine($"total-chars(['alpha','beta','gamma']) = {charsResult}");
                if (charsResult != 14)
                {
                    Console.Error.WriteLine($"FAIL: expected 14, got {charsResult}");
                    return 1;
                }

                Console.WriteLine("PASS — strings + lists in params lower round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "repeater.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "repeater.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"repeater.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Repeater harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Repeater harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
