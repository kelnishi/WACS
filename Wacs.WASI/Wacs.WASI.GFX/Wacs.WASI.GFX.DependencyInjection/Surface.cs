// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Preview2Io = Wacs.WASI.Preview2.Io;
using GenSurface = Wacs.WASI.GFX.Surface;
using GenGfxCtx = Wacs.WASI.GFX.GraphicsContext;
using SpiGfx = Wacs.WASI.GFX;
using SpiTypes = Wacs.WASI.GFX.Types;

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// Direct-link impl for <c>wasi:surface.surface</c>.
    /// Parameterless-ctor + <see cref="Create"/> shape; the
    /// runtime <c>Activator.CreateInstance</c>s + calls Create.
    /// Pollables come from the shared Preview2 pollable table
    /// (per the source-gen namespace remap that points
    /// <c>wasi:io</c> at <c>Wacs.WASI.Preview2</c>), so
    /// <c>wasi:io/poll.poll</c> resolves them.
    /// </summary>
    public sealed class Surface : GenSurface.ISurface, IDisposable
    {
        private readonly WasiGfxBundle? _bundle;
        internal SpiGfx.ISurface Inner { get; private set; } = null!;
        private bool _disposed;

        static Surface()
        {
            GfxLog.Trace("DI.Surface: type loaded into AppDomain");
        }

        [Obsolete("Prefer Surface(WasiGfxBundle) — the bundle " +
            "ctor lets the impl source the backend from the bundle " +
            "rather than the AsyncLocal-scoped WasiGfxCurrent.")]
        public Surface()
        {
            GfxLog.Trace("DI.Surface: ctor invoked (ambient path)");
        }

        public Surface(WasiGfxBundle bundle)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            GfxLog.Trace("DI.Surface: ctor invoked (bundle path)");
        }

        public void Create(GenSurface.CreateDesc desc)
        {
            GfxLog.Trace("DI.Surface.Create: invoked");
            var backend = _bundle?.Configuration.Backend
                ?? WasiGfxCurrent.RequireBackend();
            uint? w = desc.Width.HasValue ? desc.Width.Value : (uint?)null;
            uint? h = desc.Height.HasValue ? desc.Height.Value : (uint?)null;
            Inner = backend.CreateSurface(w, h);
        }

        public void ConnectGraphicsContext(GenGfxCtx.IContext context)
        {
            Wacs.WASI.GFX.GfxLog.Trace(
                "DI.Surface.ConnectGraphicsContext: ctx.Type="
                + (context?.GetType()?.FullName ?? "<null>"));
            if (context is not Context ctx)
                throw new SpiTypes.WasiGfxException(
                    "surface.connect-graphics-context: foreign IContext impl, got "
                    + (context?.GetType()?.FullName ?? "<null>"));
            EnsureCreated();
            Inner.ConnectGraphicsContext(ctx.Inner);
        }

        public uint Height()
        {
            EnsureCreated();
            return Inner.Height;
        }

        public uint Width()
        {
            EnsureCreated();
            return Inner.Width;
        }

        public void RequestSetSize(Option<uint> height, Option<uint> width)
        {
            EnsureCreated();
            Inner.RequestSetSize(
                width.HasValue ? width.Value : (uint?)null,
                height.HasValue ? height.Value : (uint?)null);
        }

        // ---- subscribe-* : returns Preview2 IPollable ------------
        // Inner.Subscribe* already returns Wacs.WASI.Preview2.Io.Pollable
        // (which IS IPollable), so we can return directly.

        public Preview2Io.IPollable SubscribeResize()
        {
            Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribeResize");
            EnsureCreated();
            return Inner.SubscribeResize();
        }

        public Preview2Io.IPollable SubscribeFrame()
        {
            Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribeFrame");
            EnsureCreated();
            return Inner.SubscribeFrame();
        }

        public Preview2Io.IPollable SubscribePointerUp()
        { Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribePointerUp"); EnsureCreated(); return Inner.SubscribePointerUp(); }

        public Preview2Io.IPollable SubscribePointerDown()
        { Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribePointerDown"); EnsureCreated(); return Inner.SubscribePointerDown(); }

        public Preview2Io.IPollable SubscribePointerMove()
        { Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribePointerMove"); EnsureCreated(); return Inner.SubscribePointerMove(); }

        public Preview2Io.IPollable SubscribeKeyUp()
        { Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribeKeyUp"); EnsureCreated(); return Inner.SubscribeKeyUp(); }

        public Preview2Io.IPollable SubscribeKeyDown()
        { Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.SubscribeKeyDown"); EnsureCreated(); return Inner.SubscribeKeyDown(); }

        // ---- get-* : translate Types.X (SPI) -> Surface.X (generated)

        public Option<GenSurface.ResizeEvent> GetResize()
        {
            Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.GetResize");
            EnsureCreated();
            var ev = Inner.GetResize();
            if (ev == null) return Option<GenSurface.ResizeEvent>.None;
            return Option<GenSurface.ResizeEvent>.Some(
                new GenSurface.ResizeEvent
                {
                    Height = ev.Value.Height,
                    Width = ev.Value.Width,
                });
        }

        public Option<GenSurface.FrameEvent> GetFrame()
        {
            Wacs.WASI.GFX.GfxLog.Trace("DI.Surface.GetFrame");
            EnsureCreated();
            var ev = Inner.GetFrame();
            if (ev == null) return Option<GenSurface.FrameEvent>.None;
            return Option<GenSurface.FrameEvent>.Some(
                new GenSurface.FrameEvent { Nothing = ev.Value.Nothing });
        }

        public Option<GenSurface.PointerEvent> GetPointerUp()
            => GetPointer(Inner.GetPointerUp());
        public Option<GenSurface.PointerEvent> GetPointerDown()
            => GetPointer(Inner.GetPointerDown());
        public Option<GenSurface.PointerEvent> GetPointerMove()
            => GetPointer(Inner.GetPointerMove());

        private static Option<GenSurface.PointerEvent> GetPointer(SpiTypes.PointerEvent? ev)
        {
            if (ev == null) return Option<GenSurface.PointerEvent>.None;
            return Option<GenSurface.PointerEvent>.Some(
                new GenSurface.PointerEvent { X = ev.Value.X, Y = ev.Value.Y });
        }

        public Option<GenSurface.KeyEvent> GetKeyUp()
            => GetKey(Inner.GetKeyUp());
        public Option<GenSurface.KeyEvent> GetKeyDown()
            => GetKey(Inner.GetKeyDown());

        private static Option<GenSurface.KeyEvent> GetKey(SpiTypes.KeyEvent? ev)
        {
            if (ev == null) return Option<GenSurface.KeyEvent>.None;
            var v = ev.Value;
            // Map SPI Key enum -> generated Surface.Key enum
            // by ordinal. Both mirror the same WIT enum, so
            // the discriminant values line up.
            var key = v.Key.HasValue
                ? Option<GenSurface.Key>.Some((GenSurface.Key)(int)v.Key.Value)
                : Option<GenSurface.Key>.None;
            var text = v.Text != null
                ? Option<string>.Some(v.Text)
                : Option<string>.None;
            return Option<GenSurface.KeyEvent>.Some(
                new GenSurface.KeyEvent
                {
                    Key = key,
                    Text = text,
                    AltKey = v.AltKey,
                    CtrlKey = v.CtrlKey,
                    MetaKey = v.MetaKey,
                    ShiftKey = v.ShiftKey,
                });
        }

        private void EnsureCreated()
        {
            if (Inner == null)
                throw new SpiTypes.WasiGfxException(
                    "Surface method called before Create()");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Inner?.Dispose();
        }
    }
}
