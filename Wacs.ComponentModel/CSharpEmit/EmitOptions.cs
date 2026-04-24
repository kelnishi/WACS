// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

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
