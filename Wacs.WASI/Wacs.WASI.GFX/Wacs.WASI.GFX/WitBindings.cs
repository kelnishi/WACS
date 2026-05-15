// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.HostBinding;
using Wacs.WASI.GFX.Types;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Component-model (WIT) bindings for the v0 wasi-gfx
    /// packages: <c>wasi:graphics-context@0.0.1</c>,
    /// <c>wasi:frame-buffer@0.0.1</c>, <c>wasi:surface@0.0.1</c>.
    /// Hand-rolled per WASI.NN's <c>WitBindings.cs</c> pattern;
    /// each method names its WIT shape in the comment above the
    /// <see cref="WasmRuntime.BindHostFunction{T}"/> call so the
    /// wire layout decisions are auditable inline.
    ///
    /// <para><b>Pollable handle space:</b> surface's
    /// <c>subscribe-*</c> methods mint <c>own&lt;pollable&gt;</c>
    /// resources. v0 allocates these into
    /// <see cref="WasiGfxHost.Pollables"/> — our own table. The
    /// guest then calls <c>wasi:io/poll@0.2.0</c>'s
    /// <c>pollable.ready()</c> / <c>pollable.block()</c>, which
    /// Preview2 binds against ITS pollable table — different
    /// table, no shared handles. The phase-3 plan is to share
    /// the table via the existing
    /// <c>Wacs.WASI.Preview2.HostBinding.ResourceContext</c>
    /// surface (which exposes
    /// <c>Table&lt;Pollable&gt;()</c> publicly). For now, this
    /// limitation surfaces only when wasi-gfx surface events are
    /// actually polled by the guest — an integration concern,
    /// not a binding-correctness concern.</para>
    ///
    /// <para><b>Canonical-ABI layout facts used here:</b>
    /// <list type="bullet">
    /// <item><c>option&lt;resize-event{u32,u32}&gt;</c>:
    /// 12 bytes (1 disc + 3 pad + 4 height + 4 width), align 4.</item>
    /// <item><c>option&lt;frame-event{bool}&gt;</c>:
    /// 2 bytes (1 disc + 1 nothing), align 1.</item>
    /// <item><c>option&lt;pointer-event{f64,f64}&gt;</c>:
    /// 24 bytes (1 disc + 7 pad + 8 x + 8 y), align 8.</item>
    /// <item><c>option&lt;key-event{option&lt;key&gt;,
    /// option&lt;string&gt;, 4 bools}&gt;</c>:
    /// 24 bytes (1 disc + 3 pad + 20 payload), align 4. Payload
    /// layout: option&lt;key&gt;@0 (2B), pad@2 (2B),
    /// option&lt;string&gt;@4 (12B: 1+3+4+4), 4 bools@16..19.</item>
    /// <item><c>record create-desc { option&lt;u32&gt;,
    /// option&lt;u32&gt; }</c> as a constructor input: flat-form
    /// 4 i32 (disc, val, disc, val).</item>
    /// </list></para>
    /// </summary>
    internal static class WitBindings
    {
        private const string GraphicsContextNs = "wasi:graphics-context/graphics-context@0.0.1";
        private const string FrameBufferNs     = "wasi:frame-buffer/frame-buffer@0.0.1";
        private const string SurfaceNs         = "wasi:surface/surface@0.0.1";

        public static void Bind(WasmRuntime runtime, WasiGfxHost host)
        {
            var alloc = new Realloc(runtime);
            BindGraphicsContext(runtime, host);
            BindFrameBuffer(runtime, host, alloc);
            BindSurface(runtime, host, alloc);
        }

        // ----------------------------------------------------
        //   wasi:graphics-context/graphics-context@0.0.1
        //     resource context {
        //       constructor();
        //       get-current-buffer: func() -> abstract-buffer;
        //       present: func();
        //     }
        //     resource abstract-buffer { }
        // ----------------------------------------------------

        private static void BindGraphicsContext(WasmRuntime runtime, WasiGfxHost host)
        {
            // [constructor]context() -> own<context>
            // No args; returns handle. Wire: Func<Ctx, i32>.
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (GraphicsContextNs, "[constructor]context"),
                _ =>
                {
                    var backend = RequireBackend(host);
                    var ctx = backend.CreateContext();
                    return host.Contexts.Allocate(ctx);
                });

            // [method]context.get-current-buffer(self) -> own<abstract-buffer>
            // Wire: Func<Ctx, i32, i32> — self handle in, buffer handle out.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (GraphicsContextNs, "[method]context.get-current-buffer"),
                (_, selfH) =>
                {
                    var ctx = (IGraphicsContext)host.Contexts.Get(selfH);
                    var buf = ctx.GetCurrentBuffer();
                    return host.AbstractBuffers.Allocate(buf);
                });

            // [method]context.present(self) — no return.
            // Wire: Action<Ctx, i32>.
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (GraphicsContextNs, "[method]context.present"),
                (_, selfH) =>
                {
                    var ctx = (IGraphicsContext)host.Contexts.Get(selfH);
                    ctx.Present();
                });

            // [resource-drop]context(self) and [resource-drop]abstract-buffer(self).
            // Both Action<Ctx, i32> — handle in, no return.
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (GraphicsContextNs, "[resource-drop]context"),
                (_, h) => host.Contexts.Drop(h));

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (GraphicsContextNs, "[resource-drop]abstract-buffer"),
                (_, h) => host.AbstractBuffers.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:frame-buffer/frame-buffer@0.0.1
        //     resource device {
        //       constructor();
        //       connect-graphics-context: func(context: borrow<context>);
        //     }
        //     resource buffer {
        //       from-graphics-buffer: static func(buffer: abstract-buffer) -> buffer;
        //       get: func() -> list<u8>;
        //       set: func(val: list<u8>);
        //     }
        // ----------------------------------------------------

        private static void BindFrameBuffer(WasmRuntime runtime, WasiGfxHost host, Realloc alloc)
        {
            // [constructor]device() -> own<device>
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (FrameBufferNs, "[constructor]device"),
                _ =>
                {
                    var backend = RequireBackend(host);
                    var dev = backend.CreateFrameBufferDevice();
                    return host.FrameBufferDevices.Allocate(dev);
                });

            // [method]device.connect-graphics-context(self, context: borrow<context>)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (FrameBufferNs, "[method]device.connect-graphics-context"),
                (_, selfH, ctxH) =>
                {
                    var dev = (IFrameBufferDevice)host.FrameBufferDevices.Get(selfH);
                    var ctx = (IGraphicsContext)host.Contexts.Get(ctxH);
                    dev.ConnectGraphicsContext(ctx);
                });

            // [static]buffer.from-graphics-buffer(buffer: abstract-buffer) -> own<buffer>
            // Parameter is own<abstract-buffer> by default (no borrow<>): the
            // static method takes ownership. We look up + drop the source
            // handle after the backend constructs the wrapper.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (FrameBufferNs, "[static]buffer.from-graphics-buffer"),
                (_, abH) =>
                {
                    // Find a device to drive — v0 contract is one device
                    // per host, so we walk the table for the first. A
                    // future cut may add a static-device or pass the
                    // device handle explicitly.
                    var dev = RequireSingleFrameBufferDevice(host);
                    var ab = (IAbstractBuffer)host.AbstractBuffers.Get(abH);
                    var buf = dev.FromGraphicsBuffer(ab);
                    // own<abstract-buffer> means we consume the handle.
                    // Don't dispose the underlying — the backend's wrapper
                    // is now responsible for the lifetime of the bytes.
                    // (TODO: revisit if backends need both alive
                    // simultaneously.)
                    host.AbstractBuffers.Drop(abH);
                    return host.FrameBufferBuffers.Allocate(buf);
                });

            // [method]buffer.get(self) -> list<u8>
            // 8-byte retArea: ptr@0, len@4.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (FrameBufferNs, "[method]buffer.get"),
                (ctx, selfH, retArea) =>
                {
                    var buf = (IFrameBufferBuffer)host.FrameBufferBuffers.Get(selfH);
                    var bytes = buf.Get();
                    WriteU8List(ctx, alloc, retArea, bytes.Span);
                });

            // [method]buffer.set(self, val: list<u8>)
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (FrameBufferNs, "[method]buffer.set"),
                (ctx, selfH, valPtr, valLen) =>
                {
                    var buf = (IFrameBufferBuffer)host.FrameBufferBuffers.Get(selfH);
                    if (valLen < 0)
                        throw new WasiGfxException("buffer.set: negative length");
                    if (valLen == 0)
                    {
                        buf.Set(ReadOnlySpan<byte>.Empty);
                        return;
                    }
                    var mem = ctx.Memory();
                    buf.Set(new ReadOnlySpan<byte>(mem, valPtr, valLen));
                });

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (FrameBufferNs, "[resource-drop]device"),
                (_, h) => host.FrameBufferDevices.Drop(h));

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (FrameBufferNs, "[resource-drop]buffer"),
                (_, h) => host.FrameBufferBuffers.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:surface/surface@0.0.1
        //     resource surface {
        //       constructor(desc: create-desc);
        //       connect-graphics-context: func(context: borrow<context>);
        //       height: func() -> u32;
        //       width: func() -> u32;
        //       request-set-size: func(height: option<u32>, width: option<u32>);
        //       subscribe-resize / subscribe-frame / subscribe-pointer-{up,down,move} /
        //         subscribe-key-{up,down}: func() -> pollable;
        //       get-resize / get-frame / get-pointer-* / get-key-*:
        //         func() -> option<{event-record}>;
        //     }
        // ----------------------------------------------------

        private static void BindSurface(WasmRuntime runtime, WasiGfxHost host, Realloc alloc)
        {
            // [constructor]surface(desc: create-desc) -> own<surface>
            // create-desc { option<u32> height, option<u32> width }
            // flat-form lowering: 4 i32 (h_disc, h_val, w_disc, w_val).
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int>>(
                (SurfaceNs, "[constructor]surface"),
                (_, hDisc, hVal, wDisc, wVal) =>
                {
                    var backend = RequireBackend(host);
                    uint? height = hDisc == 0 ? (uint?)null : (uint)hVal;
                    uint? width  = wDisc == 0 ? (uint?)null : (uint)wVal;
                    var surf = backend.CreateSurface(width, height);
                    return host.Surfaces.Allocate(surf);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface.connect-graphics-context"),
                (_, selfH, ctxH) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    var c = (IGraphicsContext)host.Contexts.Get(ctxH);
                    s.ConnectGraphicsContext(c);
                });

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface.height"),
                (_, selfH) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    return (int)s.Height;
                });

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface.width"),
                (_, selfH) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    return (int)s.Width;
                });

            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (SurfaceNs, "[method]surface.request-set-size"),
                (_, selfH, hDisc, hVal, wDisc, wVal) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    uint? height = hDisc == 0 ? (uint?)null : (uint)hVal;
                    uint? width  = wDisc == 0 ? (uint?)null : (uint)wVal;
                    s.RequestSetSize(width, height);
                });

            // --- subscribe-* (7) returning own<pollable> ---
            BindSubscribe(runtime, host, "subscribe-resize",
                s => s.SubscribeResize());
            BindSubscribe(runtime, host, "subscribe-frame",
                s => s.SubscribeFrame());
            BindSubscribe(runtime, host, "subscribe-pointer-up",
                s => s.SubscribePointerUp());
            BindSubscribe(runtime, host, "subscribe-pointer-down",
                s => s.SubscribePointerDown());
            BindSubscribe(runtime, host, "subscribe-pointer-move",
                s => s.SubscribePointerMove());
            BindSubscribe(runtime, host, "subscribe-key-up",
                s => s.SubscribeKeyUp());
            BindSubscribe(runtime, host, "subscribe-key-down",
                s => s.SubscribeKeyDown());

            // --- get-* (7) returning option<event-record> ---

            // get-resize -> option<resize-event{u32,u32}>: 12-byte retArea.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface.get-resize"),
                (ctx, selfH, retArea) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    var ev = s.GetResize();
                    WriteOptionResizeEvent(ctx, retArea, ev);
                });

            // get-frame -> option<frame-event{bool}>: 2-byte retArea, align 1.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface.get-frame"),
                (ctx, selfH, retArea) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    var ev = s.GetFrame();
                    WriteOptionFrameEvent(ctx, retArea, ev);
                });

            // get-pointer-{up,down,move} -> option<pointer-event{f64,f64}>:
            // 24-byte retArea, align 8.
            BindGetPointer(runtime, host, "get-pointer-up",
                s => s.GetPointerUp());
            BindGetPointer(runtime, host, "get-pointer-down",
                s => s.GetPointerDown());
            BindGetPointer(runtime, host, "get-pointer-move",
                s => s.GetPointerMove());

            // get-key-{up,down} -> option<key-event>: 24-byte retArea, align 4.
            BindGetKey(runtime, host, alloc, "get-key-up",
                s => s.GetKeyUp());
            BindGetKey(runtime, host, alloc, "get-key-down",
                s => s.GetKeyDown());

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (SurfaceNs, "[resource-drop]surface"),
                (_, h) => host.Surfaces.Drop(h));
        }

        // ----- subscribe-* helper (7 identical bindings) -----

        private static void BindSubscribe(WasmRuntime runtime, WasiGfxHost host,
            string methodName, Func<ISurface, Pollable> fetch)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface." + methodName),
                (_, selfH) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    var p = fetch(s);
                    return host.Pollables.Allocate(p);
                });
        }

        // ----- get-pointer-* helper (3 identical bindings) -----

        private static void BindGetPointer(WasmRuntime runtime, WasiGfxHost host,
            string methodName, Func<ISurface, PointerEvent?> fetch)
        {
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface." + methodName),
                (ctx, selfH, retArea) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    WriteOptionPointerEvent(ctx, retArea, fetch(s));
                });
        }

        // ----- get-key-* helper (2 identical bindings) -----

        private static void BindGetKey(WasmRuntime runtime, WasiGfxHost host, Realloc alloc,
            string methodName, Func<ISurface, KeyEvent?> fetch)
        {
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (SurfaceNs, "[method]surface." + methodName),
                (ctx, selfH, retArea) =>
                {
                    var s = (ISurface)host.Surfaces.Get(selfH);
                    WriteOptionKeyEvent(ctx, alloc, retArea, fetch(s));
                });
        }

        // ====================================================
        //   Backend / table helpers
        // ====================================================

        private static IBackend RequireBackend(WasiGfxHost host)
        {
            if (host.Backend == null)
                throw new WasiGfxException(
                    "wasi-gfx host has no backend configured — "
                    + "constructor or factory call cannot proceed.");
            return host.Backend;
        }

        // v0 SPI is one frame-buffer device per host, mirroring the
        // wasi-gfx WIT's "single device" implicit contract. Multi-
        // device support lands when the WIT clarifies it.
        private static IFrameBufferDevice RequireSingleFrameBufferDevice(WasiGfxHost host)
        {
            if (host.FrameBufferDevices.Count == 0)
                throw new WasiGfxException(
                    "[static]buffer.from-graphics-buffer called before any "
                    + "frame-buffer.device was constructed.");
            // ResourceTable doesn't expose enumeration; the table is
            // single-writer + has at most one entry here, so handle 1
            // is the device.
            return (IFrameBufferDevice)host.FrameBufferDevices.Get(1);
        }

        // ====================================================
        //   Canonical-ABI option / record write helpers
        // ====================================================

        // option<resize-event{u32 height, u32 width}>: 12 bytes, align 4.
        // disc@0 + 3 pad + height@4 + width@8.
        private static void WriteOptionResizeEvent(ExecContext ctx, int retArea, ResizeEvent? ev)
        {
            var mem = ctx.Memory();
            if (ev == null)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                // payload bytes left undefined per canonical-ABI; zero
                // for determinism.
                ctx.WriteI32LE(retArea + 4, 0);
                ctx.WriteI32LE(retArea + 8, 0);
                return;
            }
            var v = ev.Value;
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            ctx.WriteI32LE(retArea + 4, (int)v.Height);
            ctx.WriteI32LE(retArea + 8, (int)v.Width);
        }

        // option<frame-event{bool nothing}>: 2 bytes, align 1.
        private static void WriteOptionFrameEvent(ExecContext ctx, int retArea, FrameEvent? ev)
        {
            var mem = ctx.Memory();
            if (ev == null)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = ev.Value.Nothing ? (byte)1 : (byte)0;
        }

        // option<pointer-event{f64 x, f64 y}>: 24 bytes, align 8.
        // disc@0 + 7 pad + x@8 + y@16.
        private static void WriteOptionPointerEvent(ExecContext ctx, int retArea, PointerEvent? ev)
        {
            var mem = ctx.Memory();
            if (ev == null)
            {
                for (int i = 0; i < 8; i++) mem[retArea + i] = 0;
                ctx.WriteF64LE(retArea + 8, 0);
                ctx.WriteF64LE(retArea + 16, 0);
                return;
            }
            var v = ev.Value;
            mem[retArea] = 1;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            ctx.WriteF64LE(retArea + 8, v.X);
            ctx.WriteF64LE(retArea + 16, v.Y);
        }

        // option<key-event>: 24 bytes, align 4. Layout:
        //   disc@0 + 3 pad + key-event@4 (20 bytes)
        // key-event layout (relative to its start):
        //   option<key>@0 (2B: disc, value)
        //   pad@2 (2B)
        //   option<string>@4 (12B: disc, 3pad, ptr, len)
        //   alt@16, ctrl@17, meta@18, shift@19
        private static void WriteOptionKeyEvent(
            ExecContext ctx, Realloc alloc, int retArea, KeyEvent? ev)
        {
            var mem = ctx.Memory();
            if (ev == null)
            {
                // Zero the whole 24 bytes for determinism.
                for (int i = 0; i < 24; i++) mem[retArea + i] = 0;
                return;
            }
            var v = ev.Value;
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            int evBase = retArea + 4;

            // option<key> @ evBase + 0
            if (v.Key == null)
            {
                mem[evBase] = 0;
                mem[evBase + 1] = 0;
            }
            else
            {
                mem[evBase] = 1;
                mem[evBase + 1] = (byte)v.Key.Value;
            }
            // pad @ evBase + 2..3
            mem[evBase + 2] = 0;
            mem[evBase + 3] = 0;

            // option<string> @ evBase + 4
            if (v.Text == null)
            {
                mem[evBase + 4] = 0;
                mem[evBase + 5] = 0;
                mem[evBase + 6] = 0;
                mem[evBase + 7] = 0;
                ctx.WriteI32LE(evBase + 8, 0);
                ctx.WriteI32LE(evBase + 12, 0);
            }
            else
            {
                mem[evBase + 4] = 1;
                mem[evBase + 5] = 0;
                mem[evBase + 6] = 0;
                mem[evBase + 7] = 0;
                var bytes = Encoding.UTF8.GetBytes(v.Text);
                int strPtr = bytes.Length == 0 ? 0 : alloc.Allocate(1, bytes.Length);
                if (bytes.Length > 0)
                {
                    var m = ctx.Memory();
                    for (int i = 0; i < bytes.Length; i++)
                        m[strPtr + i] = bytes[i];
                }
                ctx.WriteI32LE(evBase + 8, strPtr);
                ctx.WriteI32LE(evBase + 12, bytes.Length);
            }

            // modifier bools @ evBase + 16..19
            mem[evBase + 16] = v.AltKey   ? (byte)1 : (byte)0;
            mem[evBase + 17] = v.CtrlKey  ? (byte)1 : (byte)0;
            mem[evBase + 18] = v.MetaKey  ? (byte)1 : (byte)0;
            mem[evBase + 19] = v.ShiftKey ? (byte)1 : (byte)0;
        }

        // list<u8> writer for buffer.get: 8-byte retArea (ptr, len).
        // Allocates via cabi_realloc + copies bytes into guest memory.
        private static void WriteU8List(ExecContext ctx, Realloc alloc,
            int retArea, ReadOnlySpan<byte> bytes)
        {
            int ptr = bytes.Length == 0 ? 0 : alloc.Allocate(1, bytes.Length);
            if (bytes.Length > 0)
            {
                var mem = ctx.Memory();
                for (int i = 0; i < bytes.Length; i++)
                    mem[ptr + i] = bytes[i];
            }
            ctx.WriteI32LE(retArea, ptr);
            ctx.WriteI32LE(retArea + 4, bytes.Length);
        }
    }
}
