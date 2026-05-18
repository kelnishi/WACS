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

namespace WitHarnessSpike.StringVariant.Generated.Validate
{
    /// <summary>
    /// Validates strings as variant payloads:
    /// <c>variant message { hello(string), silence }</c>.
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
                    Namespace = "WitHarnessSpike.StringVariant.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.StringVariant.Generated.MessagesHarness")
                    ?? throw new InvalidOperationException("MessagesHarness not found.");
                var messageType = asm.GetType("WitHarnessSpike.StringVariant.Generated.Message")
                    ?? throw new InvalidOperationException("Message type not found.");
                var helloType = asm.GetType("WitHarnessSpike.StringVariant.Generated.Message+Hello")
                    ?? throw new InvalidOperationException("Message.Hello not found.");
                var silenceType = asm.GetType("WitHarnessSpike.StringVariant.Generated.Message+Silence")
                    ?? throw new InvalidOperationException("Message.Silence not found.");

                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("LoadFrom not found.");
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })
                    ?? throw new InvalidOperationException("LoadFrom returned null.");

                var pick = harnessType.GetMethod("Pick",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Pick method not found.");

                // Test 1: want-hello = 1 → Hello("Hello, World!")
                var resultHello = pick.Invoke(harness, new object?[] { (uint)1 })!;
                Console.WriteLine($"pick(1) = {resultHello.GetType().Name}");
                if (!helloType.IsInstanceOfType(resultHello))
                {
                    Console.Error.WriteLine($"FAIL: expected Hello, got {resultHello.GetType().Name}");
                    return 1;
                }
                var greeting = (string)helloType.GetProperty("Value")!.GetValue(resultHello)!;
                Console.WriteLine($"  payload = '{greeting}'");
                if (greeting != "Hello, World!")
                {
                    Console.Error.WriteLine($"FAIL: expected 'Hello, World!', got '{greeting}'");
                    return 1;
                }

                // Test 2: want-hello = 0 → Silence
                var resultSilence = pick.Invoke(harness, new object?[] { (uint)0 })!;
                Console.WriteLine($"pick(0) = {resultSilence.GetType().Name}");
                if (!silenceType.IsInstanceOfType(resultSilence))
                {
                    Console.Error.WriteLine($"FAIL: expected Silence, got {resultSilence.GetType().Name}");
                    return 1;
                }

                Console.WriteLine("PASS — string in variant payload lift round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "messages.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "messages.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"messages.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Messages harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Messages harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
