// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.Webgpu;
using Wacs.WASI.Preview2;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter for the
    /// CLI's <c>--wasi-gfx</c> / <c>--bind</c> path. The CLI
    /// resolves bindings by assembly name, activates every
    /// public parameterless-ctor <see cref="IBindable"/> type
    /// it finds, and calls <see cref="BindToRuntime"/>.
    /// Mirrors <c>WasiNNOnnxBindable</c> in shape.
    ///
    /// <para>Wires Preview2 alongside wasi-gfx: surface
    /// pollables live in Preview2's <see cref="ResourceContext"/>
    /// so that the guest's <c>wasi:io/poll@0.2.0.poll</c>
    /// (bound by Preview2's <c>IoBindings</c>) finds them. The
    /// two hosts share one <c>ResourceContext</c> across all
    /// resource types, not just pollables.</para>
    ///
    /// <para>Exposes the constructed <see cref="Backend"/> and
    /// <see cref="Host"/> after <see cref="BindToRuntime"/> so
    /// the CLI's <c>--windowed</c> path can find the SDL event
    /// pump entry point to drive from the main thread.</para>
    /// </summary>
    public sealed class WasiGfxSilkBindable : IBindable, IDisposable
    {
        public SilkGfxBackend? Backend { get; private set; }
        public WasiGfxHost? Host { get; private set; }
        public WasiPreview2Host? Preview2Host { get; private set; }
        public ResourceContext? SharedResources { get; private set; }

        /// <summary>The matching webgpu backend + host.
        /// Constructed alongside the CPU pair so the same Silk
        /// bindable serves wasi:graphics-context /
        /// wasi:frame-buffer / wasi:surface (CPU) AND
        /// wasi:webgpu (GPU) imports through one <c>--bind</c>.
        /// <see cref="GpuBackend"/> is null only when
        /// <see cref="BindToRuntime"/> hasn't run yet.</summary>
        public SilkGpuBackend? GpuBackend { get; private set; }
        public WasiWebgpuHost? WebgpuHost { get; private set; }

        /// <summary>
        /// Optional pre-constructed backend the CLI's
        /// <c>--windowed</c> path injects so the main thread can
        /// own the backend's <c>RunMainLoop</c> while the wasm
        /// guest binds on a worker. Reflection-discoverable so
        /// <c>Wacs.Console</c> doesn't need a hard ref to this
        /// assembly. When non-null at <see cref="BindToRuntime"/>
        /// time, the bindable uses this instance and clears the
        /// preset; otherwise it constructs a fresh backend per
        /// bind (the standalone-embedder path).
        /// </summary>
        public static SilkGfxBackend? PresetBackend { get; set; }

        public void BindToRuntime(WasmRuntime runtime)
        {
            Wacs.WASI.GFX.GfxLog.Trace(
                "WasiGfxSilkBindable.BindToRuntime: entry");
            Backend = PresetBackend ?? new SilkGfxBackend();
            // Preset is single-use — if a second bind runs on the
            // same process, it gets a fresh backend rather than
            // re-using the windowed-mode one.
            PresetBackend = null;

            // Wire Preview2 first so its WitBindings handlers
            // for wasi:io/poll are registered against the shared
            // ResourceContext; then wire wasi-gfx pointing at the
            // same context. Order matters: wasi-gfx's surface
            // .subscribe-* mints pollables INTO Preview2's table.
            SharedResources = new ResourceContext();
            Preview2Host = new WasiPreview2Host(new WasiPreview2HostBuilder
            {
                SharedResources = SharedResources,
                // Bind the top-level wasi:io/poll.poll function —
                // wasi-gfx surfaces need it. PollSource is the
                // stock implementation: walks pollables, blocks on
                // their AsTask() futures when none are ready.
                Poll = new PollSource(),
            });
            Preview2Host.BindToRuntime(runtime);

            Host = runtime.UseWasiGfx(b => b
                .WithBackend(Backend)
                .WithSharedResources(SharedResources));

            // Wire the webgpu sibling alongside. The webgpu host's
            // [static]gpu-texture.from-graphics-buffer needs to
            // resolve an abstract-buffer wasm handle to the host-
            // side IAbstractBuffer that lives in the wasi-gfx
            // sibling host's table. Bridge by closure since both
            // hosts share this bindable's scope. Guests that don't
            // import wasi:webgpu/webgpu@0.0.1 never reach the
            // bridge — the bindings are registered regardless,
            // but the resolver only fires on actual GPU use.
            var cpuHost = Host;
            GpuBackend = new SilkGpuBackend();
            WebgpuHost = runtime.UseWasiWebgpu(b => b
                .WithBackend(GpuBackend)
                .WithSharedResources(SharedResources)
                .WithAbstractBufferResolver(handle =>
                    cpuHost.AbstractBuffers.Get(handle)
                        as IAbstractBuffer)
                .WithGraphicsContextResolver(handle =>
                {
                    // Two storage shapes share this table:
                    //   - Interpreter path: SPI IGraphicsContext
                    //     instances allocated by the wasi:graphics-
                    //     context.context constructor binding.
                    //   - DI / transpiler path: DI Context wrappers
                    //     (already implement the wit-generated
                    //     IContext directly).
                    // Take whichever shape is in the table and
                    // present an IContext to the webgpu binding.
                    var obj = cpuHost.Contexts.Get(handle);
                    if (obj is Wacs.WASI.GFX.GraphicsContext.IContext ic)
                        return ic;
                    if (obj is Wacs.WASI.GFX.IGraphicsContext spi)
                        return new SpiContextAdapter(spi);
                    return null;
                }));

            // Install the backend on the AsyncLocal-scoped
            // WasiGfxCurrent for the duration of this
            // ExecutionContext. Resource impls that took the
            // bundle-aware ctor (Context, Surface, Device — v1
            // phase 1g) prefer that path; the [static]buffer.
            // from-graphics-buffer factory reads from here
            // because a static method can't take the bundle
            // through ctor injection.
            WasiGfxCurrent.SetBackend(Backend);

            // 1c: the explicit Assembly.Load of
            // Wacs.WASI.GFX.DependencyInjection is now driven by
            // [WacsDependencyInjectionSibling] on the Wacs.WASI.GFX
            // contract assembly. HostPackageResolver +
            // WasiPreview2RuntimeScope read that attribute on the
            // first scan and Assembly.Load the sibling for us — no
            // belt-and-suspenders here.
            Wacs.WASI.GFX.GfxLog.Trace(
                "WasiGfxSilkBindable.BindToRuntime: done");
        }

        public void Dispose()
        {
            WebgpuHost?.Dispose();
            Host?.Dispose();
            Preview2Host = null;
            SharedResources = null;
            Backend = null;
            Host = null;
            GpuBackend = null;
            WebgpuHost = null;
        }

        // Adapts an SPI IGraphicsContext to the wit-generated
        // wasi:graphics-context.context interface so the webgpu
        // device's ConnectGraphicsContext (which takes IContext)
        // can consume contexts the wasi-gfx interpreter binding
        // stored as SPI instances. Create() is a no-op — the
        // SPI instance is already constructed.
        internal sealed class SpiContextAdapter
            : Wacs.WASI.GFX.GraphicsContext.IContext
        {
            internal Wacs.WASI.GFX.IGraphicsContext Inner { get; }
            public SpiContextAdapter(Wacs.WASI.GFX.IGraphicsContext inner)
            {
                Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }
            public void Create() { /* already constructed */ }
            public Wacs.WASI.GFX.GraphicsContext.IAbstractBuffer
                GetCurrentBuffer()
            {
                var buf = Inner.GetCurrentBuffer();
                return new SpiAbstractBufferAdapter(buf);
            }
            public void Present() => Inner.Present();
        }

        private sealed class SpiAbstractBufferAdapter
            : Wacs.WASI.GFX.GraphicsContext.IAbstractBuffer
        {
            public Wacs.WASI.GFX.IAbstractBuffer Inner { get; }
            public SpiAbstractBufferAdapter(Wacs.WASI.GFX.IAbstractBuffer inner)
            {
                Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }
        }
    }
}
