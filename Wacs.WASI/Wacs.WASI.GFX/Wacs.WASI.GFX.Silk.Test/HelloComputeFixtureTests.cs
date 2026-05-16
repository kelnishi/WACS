// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.GFX.Silk;
using Wacs.WASI.GFX.Webgpu;
using Wacs.WASI.Preview2;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.GFX.Silk.Test
{
    /// <summary>
    /// End-to-end parity test for the wgpu-native compute path.
    /// Loads the <c>wasi-webgpu-hello-compute</c> fixture
    /// component, wires the Silk-backed wasi:webgpu host alongside
    /// the wasi:io plumbing the dispatch path may need, then
    /// invokes the <c>start</c> export. The guest performs an
    /// add-one compute kernel over a u32 array and traps via
    /// <c>unreachable!()</c> on any expectation mismatch — a
    /// successful return from <c>Invoke</c> means the entire chain
    /// (adapter / device / buffer / shader / pipeline / encoder /
    /// pass / submit / map-async / readback) worked end-to-end
    /// against real wgpu-native.
    ///
    /// <para>The test silently skips when wgpu-native isn't
    /// available on the runner (CI without a GPU). Locally on a
    /// machine with the wgpu-native dylib bundled by
    /// Silk.NET.WebGPU.Native.WGPU, the test runs and verifies
    /// the dispatch path.</para>
    /// </summary>
    public class HelloComputeFixtureTests
    {
        private const string FixtureName = "fixtures/hello-compute.component.wasm";

        [Fact]
        public void HelloCompute_FixtureRunsAgainstWgpuNative()
        {
            if (!TryInitWgpuBackend(out var backend, out var skipReason))
            {
                // Soft-skip — xUnit's preferred SkippableFact
                // attribute isn't in our deps; assert-fail-fast
                // with a clear reason instead, gated behind the
                // CI escape hatch the environment variable
                // provides.
                if (Environment.GetEnvironmentVariable("WACS_REQUIRE_WGPU") == "1")
                    Assert.Fail("wgpu-native unavailable but "
                        + "WACS_REQUIRE_WGPU=1: " + skipReason);
                // Otherwise, treat as "fixture exists and would
                // have run if a GPU was available" — no Assert
                // call means the test passes vacuously. Trace
                // for visibility.
                Console.WriteLine("[skip] HelloCompute_Fixture"
                    + "RunsAgainstWgpuNative: " + skipReason);
                return;
            }
            using (backend)
            {
                var bytes = File.ReadAllBytes(FixtureName);
                var resources = new ResourceContext();

                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                        // Preview2 first — wasi-webgpu mints
                        // pollables into the Preview2 table for
                        // some flows.
                        var p2 = new WasiPreview2Host(new WasiPreview2HostBuilder
                        {
                            SharedResources = resources,
                            Poll = new PollSource(),
                        });
                        p2.BindToRuntime(runtime);

                        var webgpuHost = new WasiWebgpuHost(
                            new WasiWebgpuConfiguration
                            {
                                Backend = backend,
                                SharedResources = resources,
                            });
                        webgpuHost.BindToRuntime(runtime);
                });
                // start() trap → exception. No throw means the
                // guest's expectations all passed.
                ci.Invoke("start");
            }
        }

        private static bool TryInitWgpuBackend(
            out SilkGpuBackend? backend, out string skipReason)
        {
            backend = null;
            try
            {
                var b = new SilkGpuBackend();
                // EnsureInstance forces the wgpu-native dylib
                // load — fails fast here if the library isn't
                // available or no adapter can be discovered.
                _ = b.GetType()
                    .GetMethod("EnsureInstance",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(b, null);
                backend = b;
                skipReason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                skipReason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
