// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk.NET/SDL backend for wasi-gfx. v0 scaffolding stub —
    /// the real SDL event pump, window management, and CPU pixel
    /// blit land in the next implementation pass. This stub
    /// exists so the package compiles, the CLI `--wasi-gfx` flag
    /// can resolve it via reflection, and downstream wiring
    /// (DependencyInjection bundle, CLI binding) can be written
    /// against a real assembly identity.
    ///
    /// <para>Threading: <see cref="RunMainLoop"/> blocks the
    /// calling thread by design — the OS event loop owns the
    /// main thread on macOS. Embedders use
    /// <c>wacs run --windowed</c> to route this automatically.</para>
    /// </summary>
    public sealed class SilkGfxBackend : IBackend
    {
        public IGraphicsContext CreateContext()
            => throw new NotImplementedException(
                "SilkGfxBackend is a scaffolding stub. The SDL "
                + "graphics-context implementation lands in the "
                + "next milestone.");

        public ISurface CreateSurface(uint? width, uint? height)
            => throw new NotImplementedException(
                "SilkGfxBackend is a scaffolding stub. The SDL "
                + "surface implementation lands in the next "
                + "milestone.");

        public IFrameBufferDevice CreateFrameBufferDevice()
            => throw new NotImplementedException(
                "SilkGfxBackend is a scaffolding stub. The SDL "
                + "frame-buffer implementation lands in the next "
                + "milestone.");

        public void RunMainLoop(CancellationToken ct)
            => throw new NotImplementedException(
                "SilkGfxBackend is a scaffolding stub. The SDL "
                + "event pump lands in the next milestone.");

        public void Dispose()
        {
            // Nothing to dispose in the stub.
        }
    }
}
