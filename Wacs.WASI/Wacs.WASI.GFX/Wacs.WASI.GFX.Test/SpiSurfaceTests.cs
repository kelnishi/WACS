// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX;
using Wacs.WASI.GFX.Types;
using Xunit;

namespace Wacs.WASI.GFX.Test
{
    /// <summary>
    /// SPI surface tests — the contract package's public types
    /// and the IBackend integration with WasiGfxHost. These
    /// don't touch SDL; the Silk backend's own behavior is
    /// covered by the visual parity fixture.
    /// </summary>
    public class SpiSurfaceTests
    {
        [Fact]
        public void DefaultConfiguration_HasNoBackend()
        {
            var cfg = WasiGfxConfiguration.DefaultConfiguration();
            Assert.Null(cfg.Backend);
        }

        [Fact]
        public void BindToRuntime_WithoutBackend_Throws()
        {
            var runtime = new WasmRuntime();
            var host = new WasiGfxHost();
            var ex = Assert.Throws<WasiGfxException>(() =>
                host.BindToRuntime(runtime));
            Assert.Contains("no backend configured", ex.Message);
        }

        [Fact]
        public void BindToRuntime_WithBackend_Succeeds()
        {
            var runtime = new WasmRuntime();
            var stub = new StubBackend();
            var cfg = WasiGfxConfiguration.DefaultConfiguration();
            cfg.Backend = stub;
            using var host = new WasiGfxHost(cfg);
            host.BindToRuntime(runtime);
            Assert.Same(stub, host.Backend);
        }

        [Fact]
        public void UseWasiGfx_BuilderConfiguresBackend()
        {
            var runtime = new WasmRuntime();
            var stub = new StubBackend();
            using var host = runtime.UseWasiGfx(b => b.WithBackend(stub));
            Assert.Same(stub, host.Backend);
        }

        [Fact]
        public void UseWasiGfx_NoBuilderArgs_ThrowsAtBind()
        {
            var runtime = new WasmRuntime();
            // Empty configure → no backend set → BindToRuntime throws.
            Assert.Throws<WasiGfxException>(() =>
                runtime.UseWasiGfx());
        }

        [Fact]
        public void WasiGfxBuilder_WithBackend_NullThrows()
        {
            var b = new WasiGfxBuilder();
            Assert.Throws<ArgumentNullException>(() =>
                b.WithBackend(null!));
        }

        [Fact]
        public void Host_DisposeDisposesBackend()
        {
            var runtime = new WasmRuntime();
            var stub = new StubBackend();
            var host = runtime.UseWasiGfx(b => b.WithBackend(stub));
            Assert.False(stub.Disposed);
            host.Dispose();
            Assert.True(stub.Disposed);
        }

        [Fact]
        public void ResizeEvent_RoundtripsValues()
        {
            var ev = new ResizeEvent(1280, 720);
            Assert.Equal(1280u, ev.Width);
            Assert.Equal(720u, ev.Height);
        }

        [Fact]
        public void PointerEvent_RoundtripsValues()
        {
            var ev = new PointerEvent(12.5, 34.75);
            Assert.Equal(12.5, ev.X);
            Assert.Equal(34.75, ev.Y);
        }

        [Fact]
        public void KeyEvent_RoundtripsValues()
        {
            var ev = new KeyEvent(Key.Enter, "\n",
                altKey: true, ctrlKey: false,
                metaKey: false, shiftKey: true);
            Assert.Equal(Key.Enter, ev.Key);
            Assert.Equal("\n", ev.Text);
            Assert.True(ev.AltKey);
            Assert.False(ev.CtrlKey);
            Assert.False(ev.MetaKey);
            Assert.True(ev.ShiftKey);
        }

        [Fact]
        public void KeyEvent_NullKeyAndText_Tolerated()
        {
            // WIT uses option<key> + option<string>; both null is
            // a valid "modifier-only state changed" combination.
            var ev = new KeyEvent(null, null,
                altKey: false, ctrlKey: true,
                metaKey: false, shiftKey: false);
            Assert.Null(ev.Key);
            Assert.Null(ev.Text);
            Assert.True(ev.CtrlKey);
        }
    }
}
