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

        [System.ThreadStatic]
        private static EmitOptions? s_options;

        [System.ThreadStatic]
        private static bool s_alwaysQualifyTypeRefs;

        public static string? WorldNamespace => s_worldNs;
        public static CtInterfaceType? EmittingInterface => s_emittingIface;
        public static EmitOptions Options => s_options ?? s_default;
        public static bool IncludeWitMetadata => (s_options ?? s_default).IncludeWitMetadata;

        /// <summary>
        /// When true, <see cref="TypeRefEmit.EmitTypeRef"/> emits the
        /// fully-qualified <c>global::…</c> path for every named-type
        /// reference — even same-interface ones. Set by the Interop
        /// (sibling-static-class) emitter, where the context has no
        /// nested-scope shorthand; left off for interface-file
        /// emitters which are already inside the target scope.
        /// </summary>
        public static bool AlwaysQualifyTypeRefs => s_alwaysQualifyTypeRefs;

        private static readonly EmitOptions s_default = new EmitOptions();

        public static Scope Push(string worldNs, CtInterfaceType iface,
                                 EmitOptions? options = null,
                                 bool alwaysQualifyTypeRefs = false)
        {
            var prev = (s_worldNs, s_emittingIface, s_options, s_alwaysQualifyTypeRefs);
            s_worldNs = worldNs;
            s_emittingIface = iface;
            s_options = options;
            s_alwaysQualifyTypeRefs = alwaysQualifyTypeRefs;
            return new Scope(prev);
        }

        /// <summary>RAII restoration when emission scope ends.</summary>
        public readonly struct Scope : System.IDisposable
        {
            private readonly (string? WorldNs, CtInterfaceType? Iface, EmitOptions? Options, bool AlwaysQualify) _prev;
            public Scope((string?, CtInterfaceType?, EmitOptions?, bool) prev) { _prev = prev; }
            public void Dispose()
            {
                s_worldNs = _prev.WorldNs;
                s_emittingIface = _prev.Iface;
                s_options = _prev.Options;
                s_alwaysQualifyTypeRefs = _prev.AlwaysQualify;
            }
        }
    }
}
