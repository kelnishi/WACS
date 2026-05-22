// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.IO;
using Wacs.ComponentModel.Async;
using Wacs.WASI.Preview3;

namespace Wacs.WASI.Preview3.AotSpike
{
    /// <summary>
    /// Generated-harness consumer. The
    /// <see cref="AsyncComponentHarnessGenerator"/> emits
    /// the constructor + <see cref="Run"/> body for
    /// <see cref="RunWithErrHarness"/>. No
    /// hand-written <c>InstantiateAot</c> /
    /// <c>InvokeCoreAsyncLift</c> wiring in this file —
    /// that's the source-gen value.
    /// </summary>
    [AsyncComponentHarness]
    public partial class RunWithErrHarness
    {
        [AsyncExport(
            "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run")]
        public partial void Run();
    }

    /// <summary>
    /// WASIp3 NativeAOT spike. Loads
    /// <c>run-with-err.wasm</c> through the generator-
    /// emitted harness, drives the [callback]-lift loop,
    /// exits.
    /// </summary>
    internal static class Program
    {
        public static int Main()
        {
            try
            {
                var path = ResolveFixturePath();
                var bytes = File.ReadAllBytes(path);

                var host = new WasiPreview3Host(
                    new WasiPreview3HostBuilder());
                var harness = new RunWithErrHarness(bytes,
                    runtime => host.BindToRuntime(runtime));
                host.Dispatcher =
                    harness.Instance.AsyncDispatcher;
                harness.Run();

                Console.WriteLine(
                    "AOT-spike (generated harness): WASIp3 " +
                    "component ran through dispatcher " +
                    "without trap.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        private static string ResolveFixturePath()
        {
            var sideBySide = Path.Combine(
                AppContext.BaseDirectory,
                "run-with-err.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            throw new FileNotFoundException(
                $"run-with-err.wasm not found next to the " +
                $"published binary ({sideBySide}).");
        }
    }
}
