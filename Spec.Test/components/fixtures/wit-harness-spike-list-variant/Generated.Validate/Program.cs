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

namespace WitHarnessSpike.ListVariant.Generated.Validate
{
    /// <summary>
    /// Validates list&lt;T&gt; inside a variant payload. Streams
    /// world declares <c>variant payload { numbers(list&lt;u32&gt;),
    /// empty }</c>; the Rust impl returns Numbers or Empty based on
    /// the input flag. Tests both code paths — payload-carrying
    /// case (needs list lift + cabi_post) and unit case (no
    /// payload).
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
                    Namespace = "WitHarnessSpike.ListVariant.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.ListVariant.Generated.StreamsHarness")
                    ?? throw new InvalidOperationException("StreamsHarness not found.");
                var payloadType = asm.GetType("WitHarnessSpike.ListVariant.Generated.Payload")
                    ?? throw new InvalidOperationException("Payload type not found.");
                var numbersType = asm.GetType("WitHarnessSpike.ListVariant.Generated.Payload+Numbers")
                    ?? throw new InvalidOperationException("Payload.Numbers not found.");
                var emptyType = asm.GetType("WitHarnessSpike.ListVariant.Generated.Payload+Empty")
                    ?? throw new InvalidOperationException("Payload.Empty not found.");

                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("LoadFrom not found.");
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })
                    ?? throw new InvalidOperationException("LoadFrom returned null.");

                var getPayload = harnessType.GetMethod("GetPayload",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("GetPayload method not found.");

                // Test 1: want-numbers = 1 → Numbers([7,14,21,28])
                var resultNumbers = getPayload.Invoke(harness, new object?[] { (uint)1 })!;
                Console.WriteLine($"get-payload(1) = {resultNumbers.GetType().Name}");
                if (!numbersType.IsInstanceOfType(resultNumbers))
                {
                    Console.Error.WriteLine($"FAIL: expected Numbers, got {resultNumbers.GetType().Name}");
                    return 1;
                }
                var nums = (uint[])numbersType.GetProperty("Value")!.GetValue(resultNumbers)!;
                Console.WriteLine($"  payload values = [{string.Join(",", nums)}]");
                var expected = new uint[] { 7, 14, 21, 28 };
                if (!nums.SequenceEqual(expected))
                {
                    Console.Error.WriteLine($"FAIL: expected [7,14,21,28], got [{string.Join(",", nums)}]");
                    return 1;
                }

                // Test 2: want-numbers = 0 → Empty
                var resultEmpty = getPayload.Invoke(harness, new object?[] { (uint)0 })!;
                Console.WriteLine($"get-payload(0) = {resultEmpty.GetType().Name}");
                if (!emptyType.IsInstanceOfType(resultEmpty))
                {
                    Console.Error.WriteLine($"FAIL: expected Empty, got {resultEmpty.GetType().Name}");
                    return 1;
                }

                Console.WriteLine("PASS — list<u32> in variant payload lift round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "streams.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "streams.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"streams.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException(
                    "Streams harness does not implement WASI runtime.");

            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/error@0.2.0", "[resource-drop]error"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[resource-drop]pollable"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]input-stream"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]output-stream"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-input@0.2.0", "[resource-drop]terminal-input"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-output@0.2.0", "[resource-drop]terminal-output"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/environment@0.2.0", "get-environment"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/exit@0.2.0", "exit"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[method]pollable.block"), drop);

            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.check-write"),
                (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.blocking-flush"),
                (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.write"),
                (_, _, _, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Func<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.subscribe"),
                (_, _) => throw new NotSupportedException("stub"));

            Func<ExecContext, int> getHandle = _ =>
                throw new NotSupportedException("Streams harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);

            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
