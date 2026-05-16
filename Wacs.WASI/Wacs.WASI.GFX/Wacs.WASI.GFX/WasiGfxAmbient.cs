// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading;

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Per-AppDomain ambient handle to the active wasi-gfx
    /// backend. The transpiler-direct-link path's resource
    /// constructors run via <c>Activator.CreateInstance(impl)</c>
    /// followed by <c>impl.Create(...)</c> — a parameterless
    /// shape that can't accept an <see cref="IBackend"/>
    /// through the ctor. Concrete impls
    /// (<see cref="Context"/>, <see cref="Surface"/>, …) pull
    /// the backend from this ambient at <c>Create()</c> time.
    ///
    /// <para>Set by <c>AddWasiGfx</c> when the
    /// <see cref="WasiGfxBundle"/> is constructed, and by
    /// <c>WasiGfxSilkBindable.BindToRuntime</c> on the CLI's
    /// <c>--wasi-gfx</c> path. Single-runtime / single-process
    /// scope — multi-host setups in one AppDomain need to
    /// thread an <see cref="IBackend"/> through differently.</para>
    /// </summary>
    public static class WasiGfxAmbient
    {
        private static IBackend? _backend;

        public static IBackend? Backend => Volatile.Read(ref _backend);

        public static void SetBackend(IBackend? backend)
            => Volatile.Write(ref _backend, backend);

        internal static IBackend RequireBackend()
        {
            var b = Volatile.Read(ref _backend);
            if (b == null)
                throw new Types.WasiGfxException(
                    "WasiGfxAmbient.Backend is null — call "
                    + "AddWasiGfx(...) (or runtime.UseWasiGfx(...)) "
                    + "before the wasm guest constructs any wasi-gfx "
                    + "resource.");
            return b;
        }
    }
}
