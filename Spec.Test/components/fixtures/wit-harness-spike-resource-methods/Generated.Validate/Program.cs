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

namespace WitHarnessSpike.ResourceMethods.Generated.Validate
{
    /// <summary>
    /// Slice E — resource methods. The counter resource declares a
    /// constructor (factory method on harness), two instance methods
    /// (harness methods taking the resource as first arg in v1; nested
    /// on the resource class is a future refactor), and a static
    /// method (also flat on harness for v1).
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
                    Namespace = "WitHarnessSpike.ResourceMethods.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.ResourceMethods.Generated.DemoHarness")!;
                var counterType = asm.GetType(
                    "WitHarnessSpike.ResourceMethods.Generated.WacsResourceMethodsSpikeCounter.Counter")!;
                Console.WriteLine($"Counter = {counterType.FullName}");

                Console.WriteLine("Resource-related methods on harness:");
                foreach (var m in harnessType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (m.Name.Contains("Counter"))
                        Console.WriteLine("  " + m.Name + " -> " + m.ReturnType.Name);

                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // Static method: WacsResourceMethodsSpikeCounter_Counter_Merge(uint, uint) -> uint
                var merge = harnessType.GetMethod(
                    "WacsResourceMethodsSpikeCounter_Counter_Merge",
                    BindingFlags.Public | BindingFlags.Instance);
                if (merge == null)
                {
                    Console.Error.WriteLine("FAIL: static method Merge not found");
                    return 1;
                }
                var sum = (uint)merge.Invoke(harness, new object?[] { (uint)40, (uint)2 })!;
                Console.WriteLine($"Counter.merge(40, 2) = {sum}");
                if (sum != 42)
                {
                    Console.Error.WriteLine($"FAIL: expected 42, got {sum}");
                    return 1;
                }

                // Constructor: WacsResourceMethodsSpikeCounter_NewCounter(uint) -> Counter
                var newCounter = harnessType.GetMethod(
                    "WacsResourceMethodsSpikeCounter_NewCounter",
                    BindingFlags.Public | BindingFlags.Instance);
                if (newCounter == null)
                {
                    Console.Error.WriteLine("FAIL: constructor NewCounter not found");
                    return 1;
                }
                var counter = newCounter.Invoke(harness, new object?[] { (uint)10 })!;
                Console.WriteLine($"new Counter(10) = {counter} (type={counter.GetType().Name})");

                // Instance methods
                var getValue = harnessType.GetMethod(
                    "WacsResourceMethodsSpikeCounter_Counter_GetValue",
                    BindingFlags.Public | BindingFlags.Instance)!;
                var increment = harnessType.GetMethod(
                    "WacsResourceMethodsSpikeCounter_Counter_Increment",
                    BindingFlags.Public | BindingFlags.Instance)!;

                var initial = (uint)getValue.Invoke(harness, new object?[] { counter })!;
                Console.WriteLine($"  get-value() = {initial}");
                if (initial != 10)
                {
                    Console.Error.WriteLine($"FAIL: expected initial 10, got {initial}");
                    return 1;
                }

                var bumped1 = (uint)increment.Invoke(harness, new object?[] { counter })!;
                var bumped2 = (uint)increment.Invoke(harness, new object?[] { counter })!;
                var bumped3 = (uint)increment.Invoke(harness, new object?[] { counter })!;
                Console.WriteLine($"  increment×3 → {bumped1}, {bumped2}, {bumped3}");
                if (bumped1 != 11 || bumped2 != 12 || bumped3 != 13)
                {
                    Console.Error.WriteLine($"FAIL: increment sequence mismatched");
                    return 1;
                }

                var finalVal = (uint)getValue.Invoke(harness, new object?[] { counter })!;
                Console.WriteLine($"  get-value() final = {finalVal}");
                if (finalVal != 13)
                {
                    Console.Error.WriteLine($"FAIL: expected final 13, got {finalVal}");
                    return 1;
                }

                ((IDisposable)counter).Dispose();
                Console.WriteLine("  disposed");

                Console.WriteLine("PASS — resource methods (constructor, instance, static) round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "demo.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "demo.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException("demo.component.wasm not found");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            // Rep-as-handle 1:1 stubs (see resource-basic fixture).
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                ("[export]wacs:resource-methods-spike/counter", "[resource-new]counter"),
                (_, rep) => rep);
            runtime.BindHostFunction<Action<ExecContext, int>>(
                ("[export]wacs:resource-methods-spike/counter", "[resource-drop]counter"),
                (_, _) => { });
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                ("[export]wacs:resource-methods-spike/counter", "[resource-rep]counter"),
                (_, h) => h);

            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Demo harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Demo harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
