// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using Wacs.ComponentModel.Harness;
using Wacs.ComponentModel.Harness.Lib;
using Wacs.Core.Runtime;

namespace WitHarnessSpike.DirectReturns.Generated.Validate
{
    /// <summary>
    /// Exercises direct (top-level) returns of anonymous aggregate
    /// types: option&lt;T&gt;, result&lt;T,E&gt;, tuple&lt;...&gt;.
    /// Previously these had to be wrapped in a record; the slice
    /// adds per-export Lift__ret_&lt;name&gt; helpers that handle the
    /// retArea tail directly.
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
                    Namespace = "WitHarnessSpike.DirectReturns.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.DirectReturns.Generated.DirectsHarness")!;
                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // option<u32> direct return
                var findPositive = harnessType.GetMethod("FindPositive", BindingFlags.Public | BindingFlags.Instance)!;
                var some = (uint?)findPositive.Invoke(harness, new object?[] { 42 })!;
                var none = (uint?)findPositive.Invoke(harness, new object?[] { -1 });
                Console.WriteLine($"find-positive(42) = {(some == null ? "None" : some.ToString())}");
                Console.WriteLine($"find-positive(-1) = {(none == null ? "None" : none.ToString())}");
                if (some != 42 || none != null)
                {
                    Console.Error.WriteLine($"FAIL: option<u32> direct return mismatch");
                    return 1;
                }

                // option<string> direct return
                var ensureNonEmpty = harnessType.GetMethod("EnsureNonEmpty", BindingFlags.Public | BindingFlags.Instance)!;
                var got = (string?)ensureNonEmpty.Invoke(harness, new object?[] { "hi" });
                var gotEmpty = (string?)ensureNonEmpty.Invoke(harness, new object?[] { "" });
                Console.WriteLine($"ensure-non-empty('hi') = {(got == null ? "None" : "'" + got + "'")}");
                Console.WriteLine($"ensure-non-empty('') = {(gotEmpty == null ? "None" : "'" + gotEmpty + "'")}");
                if (got != "hi" || gotEmpty != null)
                {
                    Console.Error.WriteLine($"FAIL: option<string> direct return mismatch");
                    return 1;
                }

                // result<u32, string> direct return
                var parseInt = harnessType.GetMethod("ParseInt", BindingFlags.Public | BindingFlags.Instance)!;
                var ok = (WitResult<uint, string>)parseInt.Invoke(harness, new object?[] { "123" })!;
                var err = (WitResult<uint, string>)parseInt.Invoke(harness, new object?[] { "abc" })!;
                Console.WriteLine($"parse-int('123') = {ok}");
                Console.WriteLine($"parse-int('abc') = {err}");
                if (!ok.IsOk || ok.OkValue != 123)
                {
                    Console.Error.WriteLine($"FAIL: expected Ok(123), got {ok}");
                    return 1;
                }
                if (err.IsOk || !err.ErrValue.StartsWith("parse error:"))
                {
                    Console.Error.WriteLine($"FAIL: expected Err('parse error: ...'), got {err}");
                    return 1;
                }

                // tuple<u32, u32, string> direct return
                var coordNamed = harnessType.GetMethod("CoordNamed", BindingFlags.Public | BindingFlags.Instance)!;
                var t = ((uint, uint, string))coordNamed.Invoke(harness, new object?[] { (uint)7, (uint)11, "origin" })!;
                Console.WriteLine($"coord-named(7, 11, 'origin') = ({t.Item1}, {t.Item2}, '{t.Item3}')");
                if (t.Item1 != 7 || t.Item2 != 11 || t.Item3 != "origin")
                {
                    Console.Error.WriteLine($"FAIL: tuple<u32,u32,string> direct return mismatch");
                    return 1;
                }

                Console.WriteLine("PASS — direct option/result/tuple returns round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "directs.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "directs.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"directs.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Directs harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Directs harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
