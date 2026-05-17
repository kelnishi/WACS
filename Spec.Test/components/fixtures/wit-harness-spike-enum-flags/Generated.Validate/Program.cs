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

namespace WitHarnessSpike.EnumFlags.Generated.Validate
{
    /// <summary>
    /// Exercises enum + flags emission. Security world declares:
    /// <code>
    /// enum severity { info, warning, critical }
    /// flags permissions { read, write, execute, delete }
    /// record status { sev: severity, perms: permissions }
    /// export get-status: func() -> status
    /// </code>
    /// Rust returns severity=warning + perms=(read|write).
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
                    Namespace = "WitHarnessSpike.EnumFlags.Generated",
                });

                var harnessType = asm.GetType("WitHarnessSpike.EnumFlags.Generated.SecurityHarness")!;
                var statusType = asm.GetType("WitHarnessSpike.EnumFlags.Generated.Status")!;
                var severityType = asm.GetType("WitHarnessSpike.EnumFlags.Generated.Severity")!;
                var permissionsType = asm.GetType("WitHarnessSpike.EnumFlags.Generated.Permissions")!;

                Console.WriteLine($"Severity = {severityType} (underlying={Enum.GetUnderlyingType(severityType)}, flags={severityType.IsDefined(typeof(FlagsAttribute), false)})");
                Console.WriteLine($"Permissions = {permissionsType} (underlying={Enum.GetUnderlyingType(permissionsType)}, flags={permissionsType.IsDefined(typeof(FlagsAttribute), false)})");

                var loadFrom = harnessType.GetMethod("LoadFrom",
                    BindingFlags.Public | BindingFlags.Static)!;
                Action<WasmRuntime> bindWasi = BindWasiStubs;
                var bytes = File.ReadAllBytes(componentPath);
                var harness = loadFrom.Invoke(null, new object?[] { bytes, bindWasi })!;

                var getStatus = harnessType.GetMethod("GetStatus",
                    BindingFlags.Public | BindingFlags.Instance)!;
                var result = getStatus.Invoke(harness, Array.Empty<object?>())!;

                var sev = sampleField(statusType, result, "Sev")!;
                var perms = sampleField(statusType, result, "Perms")!;
                Console.WriteLine($"sev={sev} (numeric={Convert.ToInt32(sev)})");
                Console.WriteLine($"perms={perms} (numeric={Convert.ToInt32(perms)})");

                if (sev.ToString() != "Warning")
                {
                    Console.Error.WriteLine($"FAIL: expected sev=Warning, got {sev}");
                    return 1;
                }

                // Permissions = Read | Write — combined flag value should
                // render with both names.
                var permsStr = perms.ToString();
                if (!permsStr.Contains("Read") || !permsStr.Contains("Write"))
                {
                    Console.Error.WriteLine($"FAIL: expected perms to contain Read+Write, got '{permsStr}'");
                    return 1;
                }

                Console.WriteLine("PASS — enum + flags lift round-trip green.");
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

        private static object? sampleField(Type t, object instance, string name) =>
            t.GetProperty(name)!.GetValue(instance);

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
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "security.component.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            var devTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "wasm", "security.component.wasm"));
            if (File.Exists(devTree)) return devTree;
            throw new FileNotFoundException(
                $"security.component.wasm not found (looked at {sideBySide} and {devTree})");
        }

        private static void BindWasiStubs(WasmRuntime runtime)
        {
            Action<ExecContext, int> drop = (_, _) =>
                throw new NotSupportedException("Security harness does not implement WASI runtime.");
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
            Func<ExecContext, int> getHandle = _ => throw new NotSupportedException("Security harness does not implement WASI runtime.");
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<ExecContext, int>>(("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdin@0.2.0", "get-terminal-stdin"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stdout@0.2.0", "get-terminal-stdout"), drop);
            runtime.BindHostFunction<Action<ExecContext, int>>(("wasi:cli/terminal-stderr@0.2.0", "get-terminal-stderr"), drop);
        }
    }
}
