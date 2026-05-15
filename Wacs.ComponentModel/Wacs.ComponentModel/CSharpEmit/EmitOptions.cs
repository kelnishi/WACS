// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Configuration for <see cref="CSharpEmitter"/>. Captures the
    /// naming conventions, runtime, and version pin that together
    /// define the contract — changing these values breaks the
    /// roundtrip invariant with <c>componentize-dotnet</c>.
    /// </summary>
    public sealed class EmitOptions
    {
        /// <summary>
        /// Pinned <c>wit-bindgen-csharp</c> version whose output shape
        /// this emitter targets. When the upstream tool bumps
        /// conventions, this string changes and the reference fixtures
        /// under <c>Spec.Test/components/fixtures/</c> must be
        /// regenerated from the matching tool version.
        /// </summary>
        public const string PinnedWitBindgenCSharpVersion = "0.30.0";

        /// <summary>
        /// Component Model target runtime — matches
        /// <c>wit-bindgen c-sharp --runtime</c>. Affects which C# ABI
        /// helpers get emitted (native-aot uses different marshaling
        /// primitives than Mono).
        /// </summary>
        public CSharpRuntime Runtime { get; set; } = CSharpRuntime.NativeAot;

        /// <summary>
        /// Canonical ABI string encoding — <c>utf8</c> is the default
        /// for wit-bindgen-csharp and the Phase 1 MVP scope.
        /// </summary>
        public StringEncoding Encoding { get; set; } = StringEncoding.Utf8;

        /// <summary>
        /// When true, generated types use <c>internal</c> instead of
        /// <c>public</c>. Mirrors <c>wit-bindgen --internal</c>.
        /// </summary>
        public bool Internal { get; set; } = false;

        /// <summary>
        /// When true, skip emitting <c>cabi_realloc</c>,
        /// <c>WasmImportLinkageAttribute</c>, and the
        /// <c>_component_type.wit</c> resource. Useful when another
        /// assembly in the solution already provides them.
        /// </summary>
        public bool SkipSupportFiles { get; set; } = false;

        /// <summary>
        /// When true, emit <see cref="WitNameAttribute"/> decorators
        /// on generated types and members carrying the original
        /// kebab-case WIT name. Default <b>false</b> to preserve
        /// byte-for-byte parity with wit-bindgen-csharp 0.30.0
        /// snapshots; enable for WACS-native artifacts that need
        /// runtime reflection back to WIT identifiers.
        ///
        /// <para>Emission is additive — componentize-dotnet and
        /// downstream toolchains treat unknown attributes as
        /// metadata and don't object to their presence.</para>
        /// </summary>
        public bool IncludeWitMetadata { get; set; } = false;

        /// <summary>
        /// When true, the emitter produces <b>host-side</b>
        /// interfaces — <c>public interface IXxx { ... }</c>
        /// declarations a runtime binding implements — instead of
        /// the consumer-side instantiation classes (DllImport
        /// stubs, handle wrappers) the default mode emits.
        ///
        /// <para>Host-mode output:
        /// <list type="bullet">
        /// <item>Resources become <c>interface IResource</c> with
        /// method signatures only (no Handle, no Dispose, no
        /// DllImport).</item>
        /// <item>Free functions on a WIT interface become methods
        /// of an <c>IInterfaceName</c> interface.</item>
        /// <item>Types (<c>record</c> / <c>variant</c> /
        /// <c>enum</c> / <c>flags</c>) emit as plain DTOs in the
        /// containing namespace.</item>
        /// <item>Each generated interface and method carries a
        /// <see cref="Wacs.ComponentModel.Runtime.WitSourceAttribute"/>
        /// with the verbatim WIT-text fragment that produced it,
        /// for runtime validation against expected
        /// contracts.</item>
        /// </list></para>
        ///
        /// <para>Mutually exclusive with the consumer-side
        /// emission shape: a single emitter pass picks one mode.
        /// Use <see cref="Wacs.ComponentModel.Bindgen.WitForward"/>
        /// twice if both shapes are needed.</para>
        /// </summary>
        public bool HostInterfaceMode { get; set; } = false;

        /// <summary>
        /// Root namespace for generated host interfaces. Default
        /// derives from the WIT package name. Only consulted when
        /// <see cref="HostInterfaceMode"/> is true.
        /// </summary>
        public string? HostNamespaceOverride { get; set; }

        /// <summary>
        /// Per-WIT-package namespace remap, used when emitting
        /// cross-package type references. Map key is a WIT
        /// package qualified name like
        /// <c>wasi:io</c> (no version); value is the C# root
        /// namespace where that package's host interfaces
        /// already live in some other assembly.
        ///
        /// <para>Example: a wasi-gfx package that imports
        /// <c>wasi:io/poll.pollable</c> sets
        /// <c>{ "wasi:io" : "Wacs.WASI.Preview2" }</c> so its
        /// generated <c>ISurface.SubscribeResize()</c> returns
        /// <c>Wacs.WASI.Preview2.Io.IPollable</c> (the canonical
        /// Preview2 type) rather than re-emitting a parallel
        /// <c>Wacs.WASI.GFX.Io.IPollable</c> that would mismatch
        /// at the resource-table level.</para>
        ///
        /// <para>Only consulted for packages NOT in the local
        /// emit set — local packages always use
        /// <see cref="HostNamespaceOverride"/>.</para>
        /// </summary>
        public IReadOnlyDictionary<string, string>? PackageNamespaceMap { get; set; }
    }

    public enum CSharpRuntime
    {
        NativeAot,
        Mono,
    }

    public enum StringEncoding
    {
        Utf8,
        Utf16,
        Latin1,
    }
}
