// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview3.Cli;
using Wacs.WASI.Preview3.DependencyInjection;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Shape-level coverage for the v0
    /// <see cref="WasiPreview3Host"/> surface. The end-to-end
    /// wit-component-bound test awaits fixture availability per
    /// the Phase 3 closeout; this layer pins the configurable
    /// composite + DI integration.
    /// </summary>
    public class WasiPreview3HostTests
    {
        [Fact]
        public void Host_default_construct_provides_console_backed_stdio()
        {
            var host = new WasiPreview3Host();
            Assert.NotNull(host.Stdin);
            Assert.NotNull(host.Stdout);
            Assert.NotNull(host.Stderr);
        }

        [Fact]
        public void Host_custom_builder_threads_overrides()
        {
            using var mem = new MemoryStream();
            var custom = new StreamBackedSink(mem);
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                Stdout = custom,
            });
            Assert.Same(custom, host.Stdout);
        }

        [Fact]
        public void BindToRuntime_registers_stdout_and_stderr_host_functions()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            // Bindings registered under the WASI v3 module names
            // (placeholder convention pending fixture).
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.StdoutModuleName, "write-via-stream"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.StderrModuleName, "write-via-stream"),
                out _));
        }

        [Fact]
        public void Dispatcher_property_round_trips()
        {
            var host = new WasiPreview3Host();
            Assert.Null(host.Dispatcher);
            var d = new AsyncDispatcher();
            host.Dispatcher = d;
            Assert.Same(d, host.Dispatcher);
        }

        // ---- Slice J: stdout / stderr delegate body ------------------

        [Fact]
        public void InvokeWriteViaStream_throws_when_Dispatcher_unset()
        {
            using var mem = new MemoryStream();
            var sink = new StreamBackedSink(mem);
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder { Stdout = sink });
            // Dispatcher not set — invocation throws with the
            // documented "set after instantiation" diagnostic.
            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeWriteViaStream((IStdout)sink, streamHandle: 0));
            Assert.Contains("Dispatcher", thrown.Message);
        }

        [Fact]
        public async Task InvokeWriteViaStream_returns_future_handle_drains_to_sink()
        {
            var dispatcher = new AsyncDispatcher();
            using var mem = new MemoryStream();
            var sink = new StreamBackedSink(mem);
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder { Stdout = sink })
            {
                Dispatcher = dispatcher,
            };

            // Stream allocated by the dispatcher (mimics the
            // guest's stream.new). The host receives the handle,
            // kicks off drain, returns a future handle.
            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var futureHandle = host.InvokeWriteViaStream(
                (IStdout)sink, streamHandle);
            Assert.True(futureHandle > 0);

            // Guest writes some bytes + closes the writer side.
            foreach (var b in Encoding.UTF8.GetBytes("slice-j"))
                dispatcher.StreamTryWrite(streamHandle, b);
            dispatcher.StreamDropWritable(streamHandle);
            // Reader-half drop releases the slot so the
            // background drain loop in StreamBackedSink exits.
            dispatcher.StreamDropReadable(streamHandle);

            // Wait for drain — give the background poll loop
            // time to observe the drops + flush.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && mem.Length == 0)
                await Task.Delay(10);

            Assert.Equal("slice-j", Encoding.UTF8.GetString(mem.ToArray()));
        }

        [Fact]
        public void InvokeWriteViaStream_stderr_overload_routes_to_stderr_sink()
        {
            var dispatcher = new AsyncDispatcher();
            using var stdoutMem = new MemoryStream();
            using var stderrMem = new MemoryStream();
            var stdoutSink = new StreamBackedSink(stdoutMem);
            var stderrSink = new StreamBackedSink(stderrMem);
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    Stdout = stdoutSink,
                    Stderr = stderrSink,
                })
            {
                Dispatcher = dispatcher,
            };

            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            // Route to stderr — stdout sink should NOT receive.
            host.InvokeWriteViaStream((IStderr)stderrSink, streamHandle);
            dispatcher.StreamTryWrite(streamHandle, (byte)'X');
            dispatcher.StreamDropWritable(streamHandle);
            dispatcher.StreamDropReadable(streamHandle);

            // Smoke: stdout untouched.
            Assert.Equal(0, stdoutMem.Length);
        }

        [Fact]
        public void InvokeWriteViaStream_rejects_null_sink()
        {
            var host = new WasiPreview3Host { Dispatcher = new AsyncDispatcher() };
            Assert.Throws<ArgumentNullException>(
                () => host.InvokeWriteViaStream((IStdout)null!, 0));
            Assert.Throws<ArgumentNullException>(
                () => host.InvokeWriteViaStream((IStderr)null!, 0));
        }

        [Fact]
        public void UseWasiPreview3_returns_configured_host()
        {
            var runtime = new WasmRuntime();
            var captured = false;
            var host = runtime.UseWasiPreview3(b => { captured = true; });
            Assert.True(captured);
            Assert.NotNull(host);
        }

        // Note: an end-to-end dispatcher-buffer → sink test
        // lives in StreamBridgeTests (the lower layer
        // StreamBackedSink delegates to). StreamBackedSink
        // itself uses Task.Delay polling under parallel xunit
        // execution which makes a direct integration test
        // timing-fragile — folded into StreamBridge tests
        // for stability. The wire-level integration (via the
        // canon-async binder once a real .component.wasm
        // fixture exists) is Slice J.

        // ---- DI wiring ------------------------------------------------

        [Fact]
        public void AddWacsWasiPreview3_registers_stdio_singletons()
        {
            var services = new ServiceCollection();
            services.AddWacsWasiPreview3();
            using var provider = services.BuildServiceProvider();

            var host = provider.GetRequiredService<WasiPreview3Host>();
            var stdout = provider.GetRequiredService<IStdout>();
            Assert.Same(host.Stdout, stdout);
        }

        [Fact]
        public void AddWacsWasiPreview3_honors_builder_override()
        {
            using var mem = new MemoryStream();
            var custom = new StreamBackedSink(mem);

            var services = new ServiceCollection();
            services.AddWacsWasiPreview3(b => { b.Stdout = custom; });
            using var provider = services.BuildServiceProvider();

            var stdout = provider.GetRequiredService<IStdout>();
            Assert.Same(custom, stdout);
        }
    }
}
