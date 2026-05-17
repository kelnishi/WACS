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

namespace WitHarnessSpike.Richer.Generated.Validate
{
    /// <summary>
    /// Validation harness for Harness.Lib v0.2 — exercises the
    /// emitter's record + variant + multi-export paths. The richer
    /// fixture has no WASI imports (pure arithmetic), so LoadFrom
    /// doesn't need an import-binding callback.
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

                // Emit the harness assembly in-memory.
                var asm = HarnessEmitter.EmitInMemory(witDir, new HarnessOptions
                {
                    Namespace = "WitHarnessSpike.Richer.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.Richer.Generated.RicherHarness")
                    ?? throw new InvalidOperationException(
                        "Generated type 'WitHarnessSpike.Richer.Generated.RicherHarness' not found.");
                var vec2Type = asm.GetType("WitHarnessSpike.Richer.Generated.Vec2")
                    ?? throw new InvalidOperationException("Vec2 type not found.");
                var outcomeType = asm.GetType("WitHarnessSpike.Richer.Generated.Outcome")
                    ?? throw new InvalidOperationException("Outcome type not found.");
                var successType = asm.GetType("WitHarnessSpike.Richer.Generated.Outcome+Success")
                    ?? throw new InvalidOperationException("Outcome.Success not found.");
                var invalidType = asm.GetType("WitHarnessSpike.Richer.Generated.Outcome+Invalid")
                    ?? throw new InvalidOperationException("Outcome.Invalid not found.");

                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("LoadFrom not found.");
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, null })
                    ?? throw new InvalidOperationException("LoadFrom returned null.");

                // === Test 1: add (Vec2, Vec2) -> Vec2 ===
                var vec2Ctor = vec2Type.GetConstructor(new[] { typeof(int), typeof(int) })
                    ?? throw new InvalidOperationException("Vec2(int,int) ctor not found.");
                var a = vec2Ctor.Invoke(new object?[] { 1, 2 });
                var b = vec2Ctor.Invoke(new object?[] { 3, 4 });
                var addMethod = harnessType.GetMethod("Add",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Add method not found.");
                var sum = addMethod.Invoke(harness, new object?[] { a, b })!;
                var sumX = (int)vec2Type.GetProperty("X")!.GetValue(sum)!;
                var sumY = (int)vec2Type.GetProperty("Y")!.GetValue(sum)!;
                Console.WriteLine($"add(Vec2(1,2), Vec2(3,4)) = Vec2({sumX}, {sumY})");
                if (sumX != 4 || sumY != 6)
                {
                    Console.Error.WriteLine($"FAIL: expected Vec2(4, 6), got Vec2({sumX}, {sumY})");
                    return 1;
                }

                // === Test 2: normalize-or-fail(Vec2(0,0)) -> Outcome.Invalid ===
                var zero = vec2Ctor.Invoke(new object?[] { 0, 0 });
                var normMethod = harnessType.GetMethod("NormalizeOrFail",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("NormalizeOrFail method not found.");
                var outZero = normMethod.Invoke(harness, new object?[] { zero })!;
                Console.WriteLine($"normalize-or-fail(Vec2(0,0)) = {outZero.GetType().Name}");
                if (!invalidType.IsInstanceOfType(outZero))
                {
                    Console.Error.WriteLine($"FAIL: expected Outcome.Invalid for (0,0), got {outZero.GetType().Name}");
                    return 1;
                }

                // === Test 3: normalize-or-fail(Vec2(7, -3)) -> Outcome.Success(Vec2(1, -1)) ===
                var nonzero = vec2Ctor.Invoke(new object?[] { 7, -3 });
                var outNonzero = normMethod.Invoke(harness, new object?[] { nonzero })!;
                Console.WriteLine($"normalize-or-fail(Vec2(7, -3)) = {outNonzero.GetType().Name}");
                if (!successType.IsInstanceOfType(outNonzero))
                {
                    Console.Error.WriteLine($"FAIL: expected Outcome.Success for (7,-3), got {outNonzero.GetType().Name}");
                    return 1;
                }
                var payload = successType.GetProperty("Value")!.GetValue(outNonzero)!;
                var px = (int)vec2Type.GetProperty("X")!.GetValue(payload)!;
                var py = (int)vec2Type.GetProperty("Y")!.GetValue(payload)!;
                Console.WriteLine($"  payload Vec2({px}, {py})");
                if (px != 1 || py != -1)
                {
                    Console.Error.WriteLine($"FAIL: expected Vec2(1, -1) payload, got Vec2({px}, {py})");
                    return 1;
                }

                Console.WriteLine("PASS — record + variant paths all green.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "richer.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "richer.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"richer.component.wasm not found (looked at {sideBySide} and {devTree})");
        }
    }
}
