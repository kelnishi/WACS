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

namespace CanonResourceRoundtrip.Generated.Validate
{
    /// <summary>
    /// P2 fixture — exercises WACS's canonical-ABI resource
    /// handle-table binding end-to-end. Critically: <strong>does
    /// not stub the host-side [resource-new] / [resource-drop] /
    /// [resource-rep] imports</strong>. The runtime's
    /// BindCanonResourceImports must satisfy them; if it
    /// regresses, this fixture fails with a "function not
    /// provided by the environment" error.
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
                    Namespace = "CanonResourceRoundtrip.Generated",
                });

                var harnessType = asm.GetType("CanonResourceRoundtrip.Generated.AppHarness")!;
                var counterType = asm.GetType(
                    "CanonResourceRoundtrip.Generated.WacsCanonResourceTestCounter.Counter")!;

                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                // NO bindWasi stubs for resource intrinsics — that's
                // the point of this fixture. Just the regular WASI
                // surface (which this component doesn't use).
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                var newCounter = harnessType.GetMethod(
                    "WacsCanonResourceTestCounter_NewCounter",
                    BindingFlags.Public | BindingFlags.Instance)!;
                var bump = harnessType.GetMethod(
                    "WacsCanonResourceTestCounter_Counter_Bump",
                    BindingFlags.Public | BindingFlags.Instance)!;

                // Test 1: table allocates distinct handles for two
                // independently-constructed resources.
                var a = newCounter.Invoke(harness, new object?[] { (uint)10 })!;
                var b = newCounter.Invoke(harness, new object?[] { (uint)100 })!;
                var hA = (int)counterType.GetProperty("Handle")!.GetValue(a)!;
                var hB = (int)counterType.GetProperty("Handle")!.GetValue(b)!;
                Console.WriteLine($"new(10) -> handle {hA};  new(100) -> handle {hB}");
                if (hA == hB || hA == 0 || hB == 0)
                {
                    Console.Error.WriteLine($"FAIL: expected distinct non-zero handles, got {hA}, {hB}");
                    return 1;
                }

                // Test 2: handles are independent (resource.rep
                // lookups don't cross).
                var ba1 = (uint)bump.Invoke(harness, new object?[] { a })!;
                var bb1 = (uint)bump.Invoke(harness, new object?[] { b })!;
                Console.WriteLine($"a.bump() = {ba1}  (expect 11),  b.bump() = {bb1}  (expect 101)");
                if (ba1 != 11 || bb1 != 101)
                {
                    Console.Error.WriteLine($"FAIL: bumps mismatched");
                    return 1;
                }

                // Test 3: drop the first; the second stays alive.
                ((IDisposable)a).Dispose();
                var bb2 = (uint)bump.Invoke(harness, new object?[] { b })!;
                Console.WriteLine($"after drop(a), b.bump() = {bb2}  (expect 102)");
                if (bb2 != 102)
                {
                    Console.Error.WriteLine($"FAIL: b should still be alive after a.Dispose()");
                    return 1;
                }

                // Test 4: idempotent dispose
                ((IDisposable)a).Dispose();  // no exception
                ((IDisposable)b).Dispose();
                Console.WriteLine("disposes complete");

                // Test 5: handle freelist reuse — after a's slot was
                // freed, the next new() should reuse a's old handle.
                var c = newCounter.Invoke(harness, new object?[] { (uint)42 })!;
                var hC = (int)counterType.GetProperty("Handle")!.GetValue(c)!;
                Console.WriteLine($"after disposes, new(42) -> handle {hC}");
                if (hC != hA && hC != hB)
                {
                    Console.Error.WriteLine($"FAIL: expected freelist reuse of {hA} or {hB}, got fresh {hC}");
                    return 1;
                }
                ((IDisposable)c).Dispose();

                Console.WriteLine("PASS — WACS canon resource handle-table binding works without bindWasi stubs.");
                return 0;
            }
            catch (Exception ex)
            {
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                Console.Error.WriteLine($"FAIL: {root.GetType().Name}: {root.Message}");
                Console.Error.WriteLine(root.StackTrace);
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "app.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "app.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException("app.component.wasm not found");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            // NOTE: deliberately NO [resource-new] / [resource-drop] /
            // [resource-rep] stubs — WACS's runtime now satisfies
            // those itself. Only the regular WASI imports stay.
            Func<string, Action<ExecContext, int>> drop = label => (_, _) =>
                throw new NotSupportedException($"unexpected WASI call: {label}");
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/error@0.2.0", "[resource-drop]error"), drop("io.error.drop"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[resource-drop]pollable"), drop("io.poll.drop"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]input-stream"), drop("io.streams.in.drop"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/streams@0.2.0", "[resource-drop]output-stream"), drop("io.streams.out.drop"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-input@0.2.0", "[resource-drop]terminal-input"), drop("cli.terminal-input.drop"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-output@0.2.0", "[resource-drop]terminal-output"), drop("cli.terminal-output.drop"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/environment@0.2.0", "get-environment"), drop("cli.get-environment"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/exit@0.2.0", "exit"), drop("cli.exit"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:io/poll@0.2.0", "[method]pollable.block"), drop("io.poll.block"));
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.check-write"), (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.blocking-flush"), (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.write"), (_, _, _, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Func<ExecContext, int, int>>(("wasi:io/streams@0.2.0", "[method]output-stream.subscribe"), (_, _) => throw new NotSupportedException("stub"));
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Counter harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop("cli.terminal-stdin"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop("cli.terminal-stdout"));
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop("cli.terminal-stderr"));
        }
    }
}
