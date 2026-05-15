// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;
using System.Threading;
using Silk.NET.SDL;
using Wacs.WASI.GFX.Types;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk.NET/SDL backend for wasi-gfx v0. Implements
    /// <see cref="IBackend"/> against SDL2 via Silk.NET. Owns
    /// the SDL_Init lifecycle, a <see cref="MainThreadDispatcher"/>
    /// for marshaling window/render calls onto the main
    /// thread, and the event pump in <see cref="RunMainLoop"/>.
    ///
    /// <para>Threading: per-method calls into this backend
    /// (CreateSurface etc.) marshal through the dispatcher. If
    /// the embedder hasn't called <see cref="RunMainLoop"/>
    /// yet, the dispatcher executes inline on the caller's
    /// thread — fine on Linux/Windows for tests, racy on
    /// macOS but accepted for v0.</para>
    /// </summary>
    public sealed class SilkGfxBackend : IBackend
    {
        private readonly Sdl _sdl;
        private readonly MainThreadDispatcher _dispatcher = new MainThreadDispatcher();
        private readonly ConcurrentDictionary<uint, SilkSurface> _surfacesByWindowId
            = new ConcurrentDictionary<uint, SilkSurface>();
        private int _sdlInitialized;
        private bool _disposed;
        private int _quitRequested;

        /// <summary>
        /// Fires once on the first OS quit signal observed by the
        /// event pump — SDL_QUIT (Cmd-Q on macOS, Alt-F4 on
        /// Windows, the window-decoration close button, or a
        /// process SIGTERM that SDL forwards). Embedders subscribe
        /// to drive their cancellation: cancel the wasm task's
        /// CancellationTokenSource, signal Pollables, or
        /// <c>Environment.Exit</c>. Raises on the main (event-
        /// pump) thread. Idempotent — only fires the first time;
        /// <see cref="IsQuitRequested"/> stays true for the rest
        /// of the process lifetime.
        ///
        /// <para>v1 phase 2: replaces v0's "SDL_QUIT is ignored"
        /// stub. Subscribers must be tolerant of being invoked
        /// once and only once.</para>
        /// </summary>
        public event Action? QuitRequested;

        /// <summary>True once the event pump has observed an OS
        /// quit signal. Sticky.</summary>
        public bool IsQuitRequested
            => Volatile.Read(ref _quitRequested) != 0;

        public SilkGfxBackend()
        {
            _sdl = Sdl.GetApi();
        }

        // SDL_Init is safe to call multiple times (it
        // refcounts) but we'll guard so first-call surfaces
        // any error.
        private void EnsureSdlInit()
        {
            if (Interlocked.CompareExchange(ref _sdlInitialized, 1, 0) != 0)
                return;
            _dispatcher.Invoke(() =>
            {
                GfxLog.Trace("SilkGfxBackend: SDL_Init(VIDEO | EVENTS)");
                int rc = _sdl.Init(Sdl.InitVideo | Sdl.InitEvents);
                if (rc != 0)
                    throw new WasiGfxException(
                        "SDL_Init(VIDEO | EVENTS) failed: rc="
                        + rc);
                GfxLog.Trace("SilkGfxBackend: SDL_Init OK");
            });
        }

        /// <summary>
        /// Initialize SDL + claim the calling thread as the main
        /// thread before any worker can race the dispatcher.
        /// Required for the CLI's <c>--windowed</c> path: the
        /// embedder must call this on the main thread BEFORE
        /// kicking the wasm guest onto a worker. Subsequent
        /// dispatcher calls from worker threads will then correctly
        /// marshal back rather than racing inline on whichever
        /// thread got there first.
        ///
        /// <para>Idempotent — repeat calls are no-ops (SDL_Init
        /// is refcounted internally; ClaimMainThread re-writes
        /// the same thread id).</para>
        /// </summary>
        public void InitializeOnMainThread()
        {
            EnsureSdlInit();
            _dispatcher.ClaimMainThread();
            GfxLog.Trace("SilkGfxBackend: initialized + main thread claimed");
        }

        public IGraphicsContext CreateContext()
        {
            EnsureSdlInit();
            return new SilkContext();
        }

        public ISurface CreateSurface(uint? width, uint? height)
        {
            EnsureSdlInit();
            uint w = width  ?? 640u;
            uint h = height ?? 480u;
            var surface = new SilkSurface(_sdl, _dispatcher, w, h,
                "WACS wasi-gfx");
            _surfacesByWindowId[surface.WindowId] = surface;
            return surface;
        }

        public IFrameBufferDevice CreateFrameBufferDevice()
        {
            EnsureSdlInit();
            return new SilkFrameBufferDevice();
        }

        public void RunMainLoop(CancellationToken ct)
        {
            // Defensive: init + claim if the caller didn't go
            // through InitializeOnMainThread first. Idempotent.
            EnsureSdlInit();
            _dispatcher.ClaimMainThread();
            GfxLog.Trace("SilkGfxBackend.RunMainLoop: claimed main thread, entering pump");
            var ev = new Event();
            // Tick: drain pending dispatcher work + pump SDL events
            // + emit a frame event to every surface so guests using
            // requestAnimationFrame-style loops get woken. The
            // 16ms sleep targets ~60Hz; not precise.
            long tick = 0;
            while (!ct.IsCancellationRequested
                && !IsQuitRequested)
            {
                _dispatcher.DrainPending();

                while (_sdl.PollEvent(ref ev) == 1)
                    DispatchSdlEvent(ref ev);

                foreach (var s in _surfacesByWindowId.Values)
                    s.OnFrame();

                // Heartbeat every ~5s so log readers know the pump is alive.
                if (GfxLog.Enabled && (++tick % 300) == 0)
                    GfxLog.Trace("SilkGfxBackend.RunMainLoop: heartbeat, "
                        + "surfaces=" + _surfacesByWindowId.Count);

                try { System.Threading.Thread.Sleep(16); }
                catch (System.Threading.ThreadInterruptedException) { /* fall through */ }
            }
            GfxLog.Trace("SilkGfxBackend.RunMainLoop: cancellation observed, exiting");
        }

        private void DispatchSdlEvent(ref Event ev)
        {
            var type = (EventType)ev.Type;
            switch (type)
            {
                case EventType.Windowevent:
                    OnWindowEvent(ref ev);
                    break;
                case EventType.Mousemotion:
                    OnMouseMotion(ref ev);
                    break;
                case EventType.Mousebuttondown:
                    OnMouseButton(ref ev, down: true);
                    break;
                case EventType.Mousebuttonup:
                    OnMouseButton(ref ev, down: false);
                    break;
                case EventType.Keydown:
                    OnKey(ref ev, down: true);
                    break;
                case EventType.Keyup:
                    OnKey(ref ev, down: false);
                    break;
                case EventType.Quit:
                    // App-level quit (Cmd-Q on macOS, close
                    // button on the last-remaining window, Alt-F4
                    // on Windows, SIGTERM forwarded by SDL).
                    // Phase 2: signal subscribers and stop the
                    // event pump on the next iteration via
                    // IsQuitRequested. The embedder's QuitRequested
                    // handler is responsible for any wasm-side
                    // cleanup (cancelling the wasm task,
                    // releasing surfaces) — backend just emits
                    // the signal.
                    OnQuitSignal();
                    break;
            }
        }

        private void OnWindowEvent(ref Event ev)
        {
            // Silk.NET's Event has a Window field for SDL_WINDOWEVENT
            // — windowID is at ev.Window.WindowID.
            uint wid = ev.Window.WindowID;
            if (!_surfacesByWindowId.TryGetValue(wid, out var surface))
                return;
            var subEv = (WindowEventID)ev.Window.Event;
            switch (subEv)
            {
                case WindowEventID.SizeChanged:
                case WindowEventID.Resized:
                    surface.OnOsResize((uint)ev.Window.Data1, (uint)ev.Window.Data2);
                    break;
                case WindowEventID.Close:
                    // Phase 2: per-window close. v0 supports one
                    // surface at a time, so per-window close
                    // collapses to a process-level quit (matches
                    // the SDL_QUIT path). If/when multi-window
                    // support lands, this branch routes the close
                    // event to the specific surface and only
                    // signals quit when the last surface closes.
                    _surfacesByWindowId.TryRemove(wid, out _);
                    if (_surfacesByWindowId.IsEmpty)
                        OnQuitSignal();
                    break;
            }
        }

        // Fire QuitRequested at most once. Sticky so the pump
        // loop's next iteration exits via IsQuitRequested even if
        // the subscriber doesn't cancel the token immediately.
        private void OnQuitSignal()
        {
            if (Interlocked.CompareExchange(
                    ref _quitRequested, 1, 0) != 0)
                return;
            GfxLog.Trace("SilkGfxBackend: OS quit signal; raising "
                + "QuitRequested");
            QuitRequested?.Invoke();
        }

        private void OnMouseMotion(ref Event ev)
        {
            uint wid = ev.Motion.WindowID;
            if (!_surfacesByWindowId.TryGetValue(wid, out var surface))
                return;
            surface.OnPointerMove(ev.Motion.X, ev.Motion.Y);
        }

        private void OnMouseButton(ref Event ev, bool down)
        {
            uint wid = ev.Button.WindowID;
            if (!_surfacesByWindowId.TryGetValue(wid, out var surface))
                return;
            if (down) surface.OnPointerDown(ev.Button.X, ev.Button.Y);
            else      surface.OnPointerUp(ev.Button.X, ev.Button.Y);
        }

        private void OnKey(ref Event ev, bool down)
        {
            uint wid = ev.Key.WindowID;
            if (!_surfacesByWindowId.TryGetValue(wid, out var surface))
                return;
            var key = SdlKeyMapping.FromScancode((Scancode)ev.Key.Keysym.Scancode);
            var (alt, ctrl, meta, shift) = SdlKeyMapping.Modifiers(
                (Keymod)ev.Key.Keysym.Mod);
            // v0 leaves text=null; the IME / text-input flow
            // is wired through SDL_TEXTINPUT events which we
            // don't enable in v0 (would need SDL_StartTextInput).
            if (down) surface.OnKeyDown(key, null, alt, ctrl, meta, shift);
            else      surface.OnKeyUp(key, null, alt, ctrl, meta, shift);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _surfacesByWindowId.Values) s.Dispose();
            _surfacesByWindowId.Clear();
            if (Interlocked.CompareExchange(ref _sdlInitialized, 0, 1) == 1)
            {
                _dispatcher.Invoke(() => _sdl.Quit());
            }
            _sdl.Dispose();
        }
    }
}
