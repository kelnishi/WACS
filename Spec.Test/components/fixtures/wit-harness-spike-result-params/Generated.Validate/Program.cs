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

namespace WitHarnessSpike.ResultParams.Generated.Validate
{
    /// <summary>
    /// Exercises <c>result&lt;T, E&gt;</c> as direct param — three
    /// shapes: matching-width sides (u32/u32), matching-width strings
    /// (string/string), and both-elided.
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
                    Namespace = "WitHarnessSpike.ResultParams.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.ResultParams.Generated.DispatcherHarness")!;
                var loadFrom = harnessType.GetMethod("LoadFrom", BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                // prefer-ok: Ok(7) -> 14, Err(5) -> 1005
                var preferOk = harnessType.GetMethod("PreferOk", BindingFlags.Public | BindingFlags.Instance)!;
                var ok = (uint)preferOk.Invoke(harness, new object?[] {
                    WitResult<uint, uint>.Ok(7) })!;
                var err = (uint)preferOk.Invoke(harness, new object?[] {
                    WitResult<uint, uint>.Err(5) })!;
                Console.WriteLine($"prefer-ok(Ok(7)) = {ok}");
                Console.WriteLine($"prefer-ok(Err(5)) = {err}");
                if (ok != 14 || err != 1005)
                {
                    Console.Error.WriteLine($"FAIL: prefer-ok mismatch");
                    return 1;
                }

                // render
                var render = harnessType.GetMethod("Render", BindingFlags.Public | BindingFlags.Instance)!;
                var sOk = (string)render.Invoke(harness, new object?[] {
                    WitResult<string, string>.Ok("hi") })!;
                var sErr = (string)render.Invoke(harness, new object?[] {
                    WitResult<string, string>.Err("nope") })!;
                Console.WriteLine($"render(Ok('hi')) = '{sOk}'");
                Console.WriteLine($"render(Err('nope')) = '{sErr}'");
                if (sOk != "ok(hi)" || sErr != "err(nope)")
                {
                    Console.Error.WriteLine($"FAIL: render mismatch");
                    return 1;
                }

                // note: result with both elided — just a disc
                var note = harnessType.GetMethod("Note", BindingFlags.Public | BindingFlags.Instance)!;
                var nOk = (uint)note.Invoke(harness, new object?[] {
                    WitResult<ValueTuple, ValueTuple>.Ok(default) })!;
                var nErr = (uint)note.Invoke(harness, new object?[] {
                    WitResult<ValueTuple, ValueTuple>.Err(default) })!;
                Console.WriteLine($"note(Ok) = {nOk}, note(Err) = {nErr}");
                if (nOk != 1 || nErr != 2)
                {
                    Console.Error.WriteLine($"FAIL: note mismatch");
                    return 1;
                }

                Console.WriteLine("PASS — result<T,E> direct params lower round-trip green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "dispatcher.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "dispatcher.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"dispatcher.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Dispatcher harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Dispatcher harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
