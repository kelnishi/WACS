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

namespace WitHarnessSpike.Primitives.Generated.Validate
{
    /// <summary>
    /// Exercises the full primitive width matrix in record lift:
    /// bool / s8 / u8 / s16 / u16 / s64 / u64 / f32 / f64 / char.
    /// Each field tests a different ReadHelper path through LiftEmit.
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
                    Namespace = "WitHarnessSpike.Primitives.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.Primitives.Generated.StatsHarness")!;
                var sampleType = asm.GetType("WitHarnessSpike.Primitives.Generated.Sample")!;

                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                var getSample = harnessType.GetMethod("GetSample",
                    BindingFlags.Public | BindingFlags.Instance)!;
                var result = getSample.Invoke(harness, Array.Empty<object?>())!;

                var flag = (bool)sampleType.GetProperty("Flag")!.GetValue(result)!;
                var smallS = (sbyte)sampleType.GetProperty("SmallS")!.GetValue(result)!;
                var smallU = (byte)sampleType.GetProperty("SmallU")!.GetValue(result)!;
                var medS = (short)sampleType.GetProperty("MedS")!.GetValue(result)!;
                var medU = (ushort)sampleType.GetProperty("MedU")!.GetValue(result)!;
                var bigS = (long)sampleType.GetProperty("BigS")!.GetValue(result)!;
                var bigU = (ulong)sampleType.GetProperty("BigU")!.GetValue(result)!;
                var single = (float)sampleType.GetProperty("Single")!.GetValue(result)!;
                var dbl = (double)sampleType.GetProperty("Double")!.GetValue(result)!;
                var letter = (char)sampleType.GetProperty("Letter")!.GetValue(result)!;

                Console.WriteLine($"flag={flag}");
                Console.WriteLine($"small-s={smallS}, small-u={smallU}");
                Console.WriteLine($"med-s={medS}, med-u={medU}");
                Console.WriteLine($"big-s={bigS}, big-u={bigU}");
                Console.WriteLine($"single={single}, double={dbl}");
                Console.WriteLine($"letter={letter}");

                if (flag != true) return Fail("flag", true, flag);
                if (smallS != -7) return Fail("small-s", -7, smallS);
                if (smallU != 200) return Fail("small-u", 200, smallU);
                if (medS != -1000) return Fail("med-s", -1000, medS);
                if (medU != 50000) return Fail("med-u", 50000, medU);
                if (bigS != -9_000_000_000) return Fail("big-s", -9_000_000_000L, bigS);
                if (bigU != 18_000_000_000_000_000_000UL) return Fail("big-u", 18_000_000_000_000_000_000UL, bigU);
                if (Math.Abs(single - 3.14f) > 0.001) return Fail("single", 3.14f, single);
                if (Math.Abs(dbl - 2.718281828) > 1e-9) return Fail("double", 2.718281828, dbl);
                if (letter != 'Z') return Fail("letter", 'Z', letter);

                Console.WriteLine("PASS — full primitive width matrix in record lift round-trip green.");
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

        private static int Fail<T>(string field, T expected, T actual)
        {
            Console.Error.WriteLine($"FAIL: {field} expected {expected}, got {actual}");
            return 1;
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "stats.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "stats.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"stats.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Stats harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Stats harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
