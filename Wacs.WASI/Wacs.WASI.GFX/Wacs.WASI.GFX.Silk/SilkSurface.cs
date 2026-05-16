// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Wacs.WASI.GFX.Types;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// SDL-backed wasi-gfx surface. Owns an SDL window,
    /// renderer, and a streaming RGBA8 texture sized to the
    /// surface's current dimensions. Per-event-type queues +
    /// pollables let the wasm guest poll for resize/frame/
    /// pointer/key events.
    ///
    /// <para>Dimensions are captured at construction; OS
    /// resize events update <see cref="Width"/> /
    /// <see cref="Height"/> and the texture is re-allocated
    /// on the next <see cref="Blit"/>.
    /// <see cref="RequestSetSize"/> drives a programmatic
    /// resize via SDL_SetWindowSize.</para>
    ///
    /// <para>Construction + every SDL call goes through
    /// <see cref="MainThreadDispatcher"/> so macOS's AppKit
    /// main-thread requirement is honored.</para>
    /// </summary>
    internal sealed class SilkSurface : ISurface
    {
        private readonly Sdl _sdl;
        private readonly MainThreadDispatcher _dispatcher;
        private unsafe Window* _window;
        private unsafe Renderer* _renderer;
        private unsafe Texture* _texture;
        private int _texWidth, _texHeight;

        private uint _width, _height;

        internal uint WindowId { get; private set; }

        /// <summary>The underlying SDL Window pointer. Exposed
        /// for the wgpu-side ConnectGraphicsContext path that
        /// needs to derive a Metal layer (macOS) or HWND/Xlib
        /// surface (Windows/Linux) from it.</summary>
        internal unsafe Window* NativeWindow => _window;

        /// <summary>The Sdl API entry point — needed by the
        /// wgpu side to call SDL_Metal_CreateView / GetLayer
        /// for the swap-chain surface.</summary>
        internal Sdl NativeSdl => _sdl;

        /// <summary>The main-thread dispatcher. Exposed so the
        /// GPU-side swap-chain bridge can route SDL_Metal_*
        /// calls through it — AppKit on macOS requires
        /// NSView creation/attachment on the main thread.</summary>
        internal MainThreadDispatcher Dispatcher => _dispatcher;

        internal EventChannel<ResizeEvent>  ResizeChannel       { get; } = new EventChannel<ResizeEvent>();
        internal EventChannel<FrameEvent>   FrameChannel        { get; } = new EventChannel<FrameEvent>();
        internal EventChannel<PointerEvent> PointerUpChannel    { get; } = new EventChannel<PointerEvent>();
        internal EventChannel<PointerEvent> PointerDownChannel  { get; } = new EventChannel<PointerEvent>();
        internal EventChannel<PointerEvent> PointerMoveChannel  { get; } = new EventChannel<PointerEvent>();
        internal EventChannel<KeyEvent>     KeyUpChannel        { get; } = new EventChannel<KeyEvent>();
        internal EventChannel<KeyEvent>     KeyDownChannel      { get; } = new EventChannel<KeyEvent>();

        internal unsafe SilkSurface(Sdl sdl, MainThreadDispatcher dispatcher,
            uint width, uint height, string title)
        {
            _sdl = sdl;
            _dispatcher = dispatcher;
            _width = width;
            _height = height;

            dispatcher.Invoke(() =>
            {
                GfxLog.Trace("SilkSurface.ctor: SDL_CreateWindow " + width + "x" + height);
                fixed (byte* titleBytes = System.Text.Encoding.UTF8.GetBytes(title + "\0"))
                {
                    _window = _sdl.CreateWindow(
                        titleBytes,
                        Sdl.WindowposCentered, Sdl.WindowposCentered,
                        (int)width, (int)height,
                        (uint)(WindowFlags.Resizable | WindowFlags.Shown));
                }
                if (_window == null)
                    throw new WasiGfxException(
                        "SDL_CreateWindow failed: "
                        + Marshal.PtrToStringAnsi((nint)_sdl.GetError()));
                _renderer = _sdl.CreateRenderer(_window, -1,
                    (uint)RendererFlags.Accelerated);
                if (_renderer == null)
                    throw new WasiGfxException(
                        "SDL_CreateRenderer failed: "
                        + Marshal.PtrToStringAnsi((nint)_sdl.GetError()));
                WindowId = _sdl.GetWindowID(_window);
                // Force-bring to foreground; on macOS, windows
                // created by a backgrounded SDL app sometimes
                // stay below the calling terminal otherwise.
                _sdl.RaiseWindow(_window);
                _sdl.ShowWindow(_window);
                GfxLog.Trace("SilkSurface.ctor: window_id=" + WindowId
                    + " renderer_ok=" + (_renderer != null));
            });
        }

        public uint Width => _width;
        public uint Height => _height;

        public void ConnectGraphicsContext(IGraphicsContext context)
        {
            if (context is not SilkContext sctx)
                throw new WasiGfxException(
                    "SilkSurface.ConnectGraphicsContext: context "
                    + "is not a SilkContext (cross-backend mixing "
                    + "is not supported).");
            sctx.ConnectSurface(this);
        }

        public void RequestSetSize(uint? width, uint? height)
        {
            unsafe
            {
                if (_window == null) return;
                int w = (int)(width ?? _width);
                int h = (int)(height ?? _height);
                _dispatcher.Invoke(() =>
                {
                    _sdl.SetWindowSize(_window, w, h);
                });
                // Authoritative size update arrives via the
                // window resize event from the SDL pump; don't
                // pre-emptively update Width/Height here.
            }
        }

        public Pollable SubscribeResize()      => ResizeChannel.Subscribe();
        public ResizeEvent? GetResize()        => ResizeChannel.TryDequeue();
        public Pollable SubscribeFrame()       => FrameChannel.Subscribe();
        public FrameEvent? GetFrame()          => FrameChannel.TryDequeue();
        public Pollable SubscribePointerUp()   => PointerUpChannel.Subscribe();
        public PointerEvent? GetPointerUp()    => PointerUpChannel.TryDequeue();
        public Pollable SubscribePointerDown() => PointerDownChannel.Subscribe();
        public PointerEvent? GetPointerDown()  => PointerDownChannel.TryDequeue();
        public Pollable SubscribePointerMove() => PointerMoveChannel.Subscribe();
        public PointerEvent? GetPointerMove()  => PointerMoveChannel.TryDequeue();
        public Pollable SubscribeKeyUp()       => KeyUpChannel.Subscribe();
        public KeyEvent? GetKeyUp()            => KeyUpChannel.TryDequeue();
        public Pollable SubscribeKeyDown()     => KeyDownChannel.Subscribe();
        public KeyEvent? GetKeyDown()          => KeyDownChannel.TryDequeue();

        /// <summary>
        /// Called by the SDL event pump on the main thread
        /// with an OS-reported size change. Updates the
        /// surface dimensions + fans out to pollables.
        /// </summary>
        internal void OnOsResize(uint width, uint height)
        {
            _width = width;
            _height = height;
            ResizeChannel.Push(new ResizeEvent(width, height));
        }

        internal void OnFrame()
            => FrameChannel.Push(new FrameEvent(nothing: true));

        internal void OnPointerUp(double x, double y)
            => PointerUpChannel.Push(new PointerEvent(x, y));

        internal void OnPointerDown(double x, double y)
            => PointerDownChannel.Push(new PointerEvent(x, y));

        internal void OnPointerMove(double x, double y)
            => PointerMoveChannel.Push(new PointerEvent(x, y));

        internal void OnKeyUp(Key? key, string? text,
            bool alt, bool ctrl, bool meta, bool shift)
            => KeyUpChannel.Push(new KeyEvent(key, text, alt, ctrl, meta, shift));

        internal void OnKeyDown(Key? key, string? text,
            bool alt, bool ctrl, bool meta, bool shift)
            => KeyDownChannel.Push(new KeyEvent(key, text, alt, ctrl, meta, shift));

        /// <summary>
        /// Blit the back-buffer's pixel bytes (RGBA8 packed,
        /// width*height*4 = bytes.Length) to the connected SDL
        /// window's renderer. Marshals onto the main thread.
        /// Re-allocates the streaming texture if dimensions
        /// changed since last blit (OS resize).
        /// </summary>
        internal void Blit(byte[] pixels, int width, int height)
        {
            _dispatcher.Invoke(() => BlitOnMainThread(pixels, width, height));
        }

        private unsafe void BlitOnMainThread(byte[] pixels, int width, int height)
        {
            if (_renderer == null || pixels == null || width <= 0 || height <= 0)
                return;
            if (_texture == null || width != _texWidth || height != _texHeight)
            {
                if (_texture != null)
                {
                    _sdl.DestroyTexture(_texture);
                    _texture = null;
                }
                _texture = _sdl.CreateTexture(
                    _renderer,
                    (uint)PixelFormatEnum.Abgr8888,  // byte order R,G,B,A on LE
                    (int)TextureAccess.Streaming,
                    width, height);
                if (_texture == null)
                    throw new WasiGfxException(
                        "SDL_CreateTexture failed: "
                        + Marshal.PtrToStringAnsi((nint)_sdl.GetError()));
                _texWidth = width;
                _texHeight = height;
            }
            int expected = width * height * 4;
            if (pixels.Length < expected)
                return;
            fixed (byte* p = pixels)
            {
                _sdl.UpdateTexture(_texture, (Rectangle<int>*)null,
                    (void*)p, width * 4);
            }
            _sdl.RenderClear(_renderer);
            _sdl.RenderCopy(_renderer, _texture,
                (Rectangle<int>*)null, (Rectangle<int>*)null);
            _sdl.RenderPresent(_renderer);
        }

        /// <summary>Drop the SDL renderer + cached texture so the
        /// window's content view is free for an SDL_Metal_View
        /// claim. Called by the wgpu-side ConnectGraphicsContext —
        /// the SDL renderer would otherwise hold the window's
        /// CAMetalLayer and the wgpu surface's draw would render
        /// to an orphan layer that never appears on screen.
        /// Idempotent.</summary>
        internal void DropSdlRenderer()
        {
            _dispatcher.Invoke(() =>
            {
                unsafe
                {
                    if (_texture != null)
                    {
                        _sdl.DestroyTexture(_texture);
                        _texture = null;
                    }
                    if (_renderer != null)
                    {
                        _sdl.DestroyRenderer(_renderer);
                        _renderer = null;
                    }
                }
            });
        }

        public void Dispose()
        {
            // Dispatch to main thread so SDL destroys happen
            // where they were created. Safe to call from any
            // thread (including the main thread from the
            // backend's teardown).
            _dispatcher.Invoke(() =>
            {
                unsafe
                {
                    if (_texture != null) { _sdl.DestroyTexture(_texture); _texture = null; }
                    if (_renderer != null) { _sdl.DestroyRenderer(_renderer); _renderer = null; }
                    if (_window != null) { _sdl.DestroyWindow(_window); _window = null; }
                }
            });
        }
    }
}
