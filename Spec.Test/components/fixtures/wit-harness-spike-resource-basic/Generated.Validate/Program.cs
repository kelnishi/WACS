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

namespace WitHarnessSpike.ResourceBasic.Generated.Validate
{
    /// <summary>
    /// Slice D scaffolding: a resource type is opaque from the
    /// host's perspective except for the IDisposable surface. A free
    /// function returns a resource handle; the harness wraps it as
    /// a CLR class instance with the wasm-side drop delegate
    /// captured. Calling Dispose invokes the drop via the
    /// canonical-ABI <c>[resource-drop]bucket</c> adapter.
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
                    Namespace = "WitHarnessSpike.ResourceBasic.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.ResourceBasic.Generated.AppHarness")!;
                var ifaceNs = "WitHarnessSpike.ResourceBasic.Generated.WacsResourceBasicSpikeStore";
                var bucketType = asm.GetType(ifaceNs + ".Bucket")!;
                Console.WriteLine($"Bucket = {bucketType.FullName}");
                Console.WriteLine($"Bucket implements IDisposable: {typeof(IDisposable).IsAssignableFrom(bucketType)}");

                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                var open = harnessType.GetMethod("WacsResourceBasicSpikeStore_Open",
                    BindingFlags.Public | BindingFlags.Instance);
                if (open == null)
                {
                    Console.Error.WriteLine("FAIL: open method not found");
                    foreach (var m in harnessType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        Console.Error.WriteLine("  " + m.Name);
                    return 1;
                }

                var bucket = open.Invoke(harness, new object?[] { "alpha" })!;
                Console.WriteLine($"open('alpha') = {bucket} (type={bucket.GetType().Name})");
                if (!bucketType.IsInstanceOfType(bucket))
                {
                    Console.Error.WriteLine($"FAIL: expected Bucket instance");
                    return 1;
                }

                // Check Handle property reports something
                var handleProp = bucketType.GetProperty("Handle")!;
                var handle = (int)handleProp.GetValue(bucket)!;
                Console.WriteLine($"  handle = {handle}");
                if (handle == 0)
                {
                    Console.Error.WriteLine("FAIL: handle should be non-zero after open");
                    return 1;
                }

                // Dispose drops the resource. After Dispose, Handle should be 0.
                ((IDisposable)bucket).Dispose();
                var handleAfter = (int)handleProp.GetValue(bucket)!;
                Console.WriteLine($"  handle after Dispose = {handleAfter}");
                if (handleAfter != 0)
                {
                    Console.Error.WriteLine("FAIL: handle should be 0 after Dispose");
                    return 1;
                }

                // Second Dispose should be a no-op (no double-drop).
                ((IDisposable)bucket).Dispose();
                Console.WriteLine("  Dispose() second call: no-op (no exception)");

                // Open another and use using-statement
                using (var b = (IDisposable)open.Invoke(harness, new object?[] { "beta" })!)
                {
                    var h = (int)handleProp.GetValue(b)!;
                    Console.WriteLine($"  using(open('beta')) → handle {h}");
                }
                Console.WriteLine("  using-block disposed");

                Console.WriteLine("PASS — resource scaffolding (open + Dispose) round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "app.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "app.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException("app.component.wasm not found");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            // The wasm component imports [export]<iface>.[resource-new]
            // and .[resource-drop] from the host — these are the
            // canonical-ABI handle-table intrinsics. WACS's component
            // runtime doesn't yet construct these adapters itself
            // (it counts them for index-space accounting but doesn't
            // bind), so we provide rep-as-handle stubs here: every
            // new() returns a fresh int, drop is a no-op. Without a
            // real handle table the wasm-side dtor doesn't fire from
            // drop, but the harness-side wiring (Dispose -> wasm drop
            // call -> Action<int>.Invoke) still gets exercised.
            // Rep-as-handle 1:1 — the rep IS the handle, so the
            // dtor (which expects a rep / Rust struct pointer) gets
            // a real pointer when invoked with the handle on drop.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                ("[export]wacs:resource-basic-spike/store", "[resource-new]bucket"),
                (_, rep) => rep);
            runtime.BindHostFunction<Action<ExecContext, int>>(
                ("[export]wacs:resource-basic-spike/store", "[resource-drop]bucket"),
                (_, _) => { /* no-op stub; runtime handle table TBD */ });

            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("App harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("App harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
