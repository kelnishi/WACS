// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using GenGfxCtx = Wacs.WASI.GFX.GraphicsContext;
using SpiGfx = Wacs.WASI.GFX;

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// Direct-link impl for <c>wasi:graphics-context.context</c>.
    /// Parameterless-ctor + <see cref="Create"/> shape per the
    /// transpiler's SourceGen-resource convention; the runtime
    /// <c>Activator.CreateInstance</c>s this, then calls
    /// <see cref="Create"/>, then allocates a handle via the
    /// <c>WasiPreview2Resources</c> bridge.
    /// </summary>
    public sealed class Context : GenGfxCtx.IContext, IDisposable
    {
        private readonly WasiGfxBundle? _bundle;
        internal SpiGfx.IGraphicsContext Inner { get; private set; } = null!;
        private bool _disposed;

        static Context()
        {
            GfxLog.Trace("DI.Context: type loaded into AppDomain");
        }

        /// <summary>Parameterless ctor used as a back-compat
        /// fallback (the transpiler emits this when no
        /// bundle-taking ctor is found on the impl). Current
        /// transpiler emit prefers the
        /// <see cref="Context(WasiGfxBundle)"/> ctor and threads
        /// the backend through the bundle — letting multi-runtime
        /// embedders run separate <see cref="IBackend"/>s in one
        /// process.</summary>
        [Obsolete("Prefer the bundle-taking ctor — multi-runtime " +
            "support drops the WasiGfxAmbient static. Kept for " +
            "transpiler back-compat with the earlier emit shape.")]
        public Context()
        {
            GfxLog.Trace("DI.Context: ctor invoked (Activator.CreateInstance, ambient path)");
        }

        public Context(WasiGfxBundle bundle)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            GfxLog.Trace("DI.Context: ctor invoked (bundle path)");
        }

        public void Create()
        {
            GfxLog.Trace("DI.Context.Create: invoked");
            var backend = _bundle?.Configuration.Backend
#pragma warning disable CS0618 // Type or member is obsolete
                ?? WasiGfxAmbient.RequireBackend();
#pragma warning restore CS0618
            if (backend == null)
                throw new Types.WasiGfxException(
                    "DI.Context.Create: no backend available. "
                    + "Construct via Context(WasiGfxBundle) or set "
                    + "the legacy WasiGfxAmbient.Backend.");
            Inner = backend.CreateContext();
        }

        public GenGfxCtx.IAbstractBuffer GetCurrentBuffer()
        {
            EnsureCreated();
            var ab = new AbstractBuffer();
            ab.SetInner(Inner.GetCurrentBuffer());
            return ab;
        }

        public void Present()
        {
            EnsureCreated();
            Inner.Present();
        }

        private void EnsureCreated()
        {
            if (Inner == null)
                throw new Types.WasiGfxException(
                    "Context method called before Create()");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Inner?.Dispose();
        }
    }

    /// <summary>
    /// Direct-link impl for
    /// <c>wasi:graphics-context.abstract-buffer</c>. Carries the
    /// backend's opaque buffer through to
    /// <see cref="Buffer.FromGraphicsBuffer"/>. No public methods
    /// on the WIT side; the type exists to give the canonical-ABI
    /// a resource handle to track.
    /// </summary>
    public sealed class AbstractBuffer : GenGfxCtx.IAbstractBuffer, IDisposable
    {
        internal SpiGfx.IAbstractBuffer? Inner { get; private set; }
        private bool _disposed;

        internal void SetInner(SpiGfx.IAbstractBuffer inner) { Inner = inner; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Inner?.Dispose();
        }
    }
}
