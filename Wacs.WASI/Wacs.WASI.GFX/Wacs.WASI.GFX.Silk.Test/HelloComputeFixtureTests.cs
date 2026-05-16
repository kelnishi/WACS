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
using P2Cli = Wacs.WASI.Preview2.Cli;
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
        [Fact]
        public void HelloCompute_FixtureRunsAgainstWgpuNative()
        {
            FixtureRunner.RunOrSkip("fixtures/hello-compute.component.wasm",
                nameof(HelloCompute_FixtureRunsAgainstWgpuNative));
        }

        [Fact]
        public void HelloRender_FixtureRunsAgainstWgpuNative()
        {
            FixtureRunner.RunOrSkip("fixtures/hello-render.component.wasm",
                nameof(HelloRender_FixtureRunsAgainstWgpuNative));
        }

        [Fact]
        public void GameOfLife_FixtureRunsAgainstWgpuNative()
        {
            FixtureRunner.RunOrSkip("fixtures/game-of-life.component.wasm",
                nameof(GameOfLife_FixtureRunsAgainstWgpuNative));
        }
    }

    /// <summary>
    /// Shared "load fixture, wire Silk webgpu, invoke start()"
    /// runner. xUnit's preferred SkippableFact attribute isn't
    /// in our deps; tests that need a GPU soft-skip when
    /// wgpu-native isn't available unless the CI escape hatch
    /// <c>WACS_REQUIRE_WGPU=1</c> is set.
    /// </summary>
    internal static class FixtureRunner
    {
        internal static void RunOrSkip(string fixturePath, string testName)
        {
            if (!TryInitWgpuBackend(out var backend, out var skipReason))
            {
                if (Environment.GetEnvironmentVariable("WACS_REQUIRE_WGPU") == "1")
                    Assert.Fail("wgpu-native unavailable but "
                        + "WACS_REQUIRE_WGPU=1: " + skipReason);
                Console.WriteLine("[skip] " + testName + ": " + skipReason);
                return;
            }
            using (backend)
            {
                var bytes = File.ReadAllBytes(fixturePath);
                var resources = new ResourceContext();
                var ci = ComponentInstance.Instantiate(bytes, runtime =>
                {
                    var p2 = new WasiPreview2Host(new WasiPreview2HostBuilder
                    {
                        SharedResources = resources,
                        Poll = new PollSource(),
                        // Wire the full wasi:cli surface so any fixture
                        // whose code size exceeds LTO's dead-code-
                        // elimination threshold (e.g. the render-pipeline
                        // fixture with depth-stencil + multisample state)
                        // can still bind every wasi:cli import the rust
                        // panic machinery drags in even when the guest
                        // never actually uses them.
                        Environment = new P2Cli.Environment(),
                        Exit = new P2Cli.ExitHandler(),
                        Stdin = new P2Cli.Stdin(),
                        Stdout = new P2Cli.Stdout(),
                        Stderr = new P2Cli.Stderr(),
                        TerminalStdin = new P2Cli.TerminalStdin(),
                        TerminalStdout = new P2Cli.TerminalStdout(),
                        TerminalStderr = new P2Cli.TerminalStderr(),
                    });
                    p2.BindToRuntime(runtime);

                    var webgpuHost = new WasiWebgpuHost(
                        new WasiWebgpuConfiguration
                        {
                            Backend = backend!,
                            SharedResources = resources,
                        });
                    webgpuHost.BindToRuntime(runtime);
                });
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
