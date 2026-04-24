// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Thread-local ambient state used by the lower-layer emitters
    /// to resolve fully qualified <c>global::</c> type names when
    /// they encounter cross-interface <see cref="CtTypeRef"/>s.
    ///
    /// <para>Public emit entry points
    /// (<see cref="CSharpEmitter.EmitExportInterfaceFile"/>,
    /// <see cref="CSharpEmitter.EmitImportInterfaceFile"/>)
    /// set these before delegating to TypeDefEmit / TypeRefEmit and
    /// reset on exit. Nested emitters read via the accessors.</para>
    ///
    /// <para>Ambient state is preferred here over threading every
    /// call site with a context argument: <c>TypeRefEmit.EmitParam</c>
    /// is called from deep inside nested type emitters (variants
    /// inside records inside resources, etc.) and the context is
    /// constant across a single emit pass.</para>
    /// </summary>
    internal static class EmitAmbient
    {
        [System.ThreadStatic]
        private static string? s_worldNs;

        [System.ThreadStatic]
        private static CtInterfaceType? s_emittingIface;

        public static string? WorldNamespace => s_worldNs;
        public static CtInterfaceType? EmittingInterface => s_emittingIface;

        public static Scope Push(string worldNs, CtInterfaceType iface)
        {
            var prev = (s_worldNs, s_emittingIface);
            s_worldNs = worldNs;
            s_emittingIface = iface;
            return new Scope(prev);
        }

        /// <summary>RAII restoration when emission scope ends.</summary>
        public readonly struct Scope : System.IDisposable
        {
            private readonly (string? WorldNs, CtInterfaceType? Iface) _prev;
            public Scope((string?, CtInterfaceType?) prev) { _prev = prev; }
            public void Dispose()
            {
                s_worldNs = _prev.WorldNs;
                s_emittingIface = _prev.Iface;
            }
        }
    }
}
