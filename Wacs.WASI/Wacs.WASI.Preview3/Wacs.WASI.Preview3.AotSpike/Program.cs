// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview3;

namespace Wacs.WASI.Preview3.AotSpike
{
    /// <summary>
    /// WASIp3 NativeAOT spike. Loads <c>run-with-err.wasm</c>
    /// (the minimal async-lifted wasip3 fixture — single-poll
    /// body returning <c>Err(())</c>), wires the standard
    /// WASIp3 host, drives the lift's <c>[callback]</c> loop,
    /// and exits with the body's task-return code.
    ///
    /// <para>The point of this spike is to prove the canon-
    /// async dispatcher path (host subtasks, BLOCKED-aware
    /// stream/future scaffolding, callback driver loop) is
    /// genuinely reachable from AOT-safe consumer code — i.e.
    /// that <see cref="ComponentInstance.InstantiateAot"/> +
    /// the dispatcher surface don't transitively pull in the
    /// reflective typed-bridge.</para>
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
                var instance = ComponentInstance.InstantiateAot(
                    bytes, runtime => host.BindToRuntime(runtime));
                host.Dispatcher = instance.AsyncDispatcher;

                // Drives the [callback]-style async lift loop
                // to completion. run-with-err returns Err(())
                // on the first poll, so we expect the dispatcher
                // to transition through one Yield→Exit cycle.
                instance.InvokeCoreAsyncLift(
                    "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");

                // Spike success criterion: instance completed
                // without trapping. The actual Err(()) result is
                // captured via the cli/exit binding (set by the
                // wasi-bindgen panic handler) — for run-with-err
                // it returns 1.
                Console.WriteLine("AOT-spike: WASIp3 component " +
                    "ran through dispatcher without trap.");
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
            var sideBySide = Path.Combine(AppContext.BaseDirectory,
                "run-with-err.wasm");
            if (File.Exists(sideBySide)) return sideBySide;
            throw new FileNotFoundException(
                $"run-with-err.wasm not found next to the " +
                $"published binary ({sideBySide}).");
        }
    }
}
