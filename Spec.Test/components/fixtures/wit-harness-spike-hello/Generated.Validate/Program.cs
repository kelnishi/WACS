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

namespace WitHarnessSpike.Generated.Validate
{
    /// <summary>
    /// Validation harness for WACS.ComponentModel.Harness.Lib.
    /// Emits HelloHarness in-memory from the spike's WIT, then drives
    /// it via reflection to confirm the generated harness produces
    /// the same output as the hand-written one in
    /// `../Aot.Spike/HelloHarness.cs`.
    ///
    /// <para>This is the closing-the-loop test for Package 1 of
    /// docs/wit-harness-plan.md — Harness.Lib emits a typed harness
    /// matching the spike's shape, and the assertion `Greet("World")
    /// == "Hello, World!"` is the diff-against-spike.</para>
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

                // 1. Emit the harness assembly in-memory.
                var asm = HarnessEmitter.EmitInMemory(witDir, new HarnessOptions
                {
                    Namespace = "WitHarnessSpike.Generated",
                });

                // 2. Find the generated type.
                var harnessType = asm.GetType("WitHarnessSpike.Generated.HelloHarness")
                    ?? throw new InvalidOperationException(
                        "Generated type 'WitHarnessSpike.Generated.HelloHarness' not found.");

                // 3. Call static `LoadFrom(byte[], Action<WasmRuntime>?)`.
                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("LoadFrom not found.");
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })
                    ?? throw new InvalidOperationException("LoadFrom returned null.");

                // 4. Call `Greet("World")`.
                var greet = harnessType.GetMethod("Greet",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Greet not found.");
                var result = (string?)greet.Invoke(harness, new object?[] { "World" });

                Console.WriteLine($"result: {result}");
                if (result != "Hello, World!")
                {
                    Console.Error.WriteLine($"FAIL: expected 'Hello, World!', got '{result}'");
                    return 1;
                }

                Console.WriteLine("PASS — generated harness matches hand-written spike output.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "hello.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "hello.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"hello.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        /// <summary>
        /// Bind throw-on-call stubs for every WASI host function the
        /// hello.component.wasm imports. Copied verbatim from the
        /// hand-written spike (Aot.Spike/HelloHarness.cs) — greet is
        /// pure string formatting so the stubs never fire, but
        /// instantiation needs them registered.
        /// </summary>
        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException(
                    "Generated hello harness does not implement WASI runtime.");

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
                throw new NotSupportedException("Generated hello harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);

            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
