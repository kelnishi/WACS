// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Declares the CLR namespace where a WIT package's generated
    /// host interfaces live. Lets downstream packages'
    /// <c>WitHostInterfaceGenerator</c> resolve cross-package
    /// type references automatically — when
    /// <c>WACS.WASI.GFX</c>'s <c>surface.wit</c> imports
    /// <c>use wasi:io/poll@0.2.0.{ pollable }</c>, the generator
    /// emits <c>global::Wacs.WASI.Preview2.Io.IPollable</c> by
    /// reading the <c>[WitPackageMapping("wasi:io",
    /// "Wacs.WASI.Preview2")]</c> attribute on the
    /// <c>Wacs.WASI.Preview2</c> assembly's metadata.
    ///
    /// <para>v1 phase 1 deferred item 1d. Replaces the project-
    /// level <c>&lt;WitHostPackageNamespaceMap&gt;</c> MSBuild
    /// item with self-describing assembly metadata. Downstream
    /// packages no longer need to know who owns the wasi:io
    /// namespace — they just reference the assembly that owns
    /// it and the source-gen reads the attribute automatically.
    /// The MSBuild item stays as an override mechanism for edge
    /// cases (e.g. a forked vendor of wasi:io that lives in a
    /// non-canonical namespace).</para>
    ///
    /// <para>Apply as a top-level assembly attribute. Stack
    /// multiples — one assembly typically owns several WIT
    /// packages (Wacs.WASI.Preview2 owns wasi:io, wasi:cli,
    /// wasi:filesystem, etc.). The generator merges every
    /// mapping it sees on every referenced assembly.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly,
        AllowMultiple = true, Inherited = false)]
    public sealed class WitPackageMappingAttribute : Attribute
    {
        /// <summary>The unversioned WIT package identifier
        /// (e.g. <c>"wasi:io"</c>, <c>"wasi:graphics-context"</c>).
        /// Version annotations (<c>@0.2.0</c>) are stripped before
        /// the lookup — minor revisions share the same CLR
        /// namespace by convention.</summary>
        public string Package { get; }

        /// <summary>The root CLR namespace where the package's
        /// generated interfaces live. The generator appends the
        /// interface-specific suffix (e.g. <c>Io.IPollable</c>)
        /// when emitting a cross-package type reference.</summary>
        public string Namespace { get; }

        public WitPackageMappingAttribute(string package, string @namespace)
        {
            Package = package ?? throw new ArgumentNullException(
                nameof(package));
            Namespace = @namespace ?? throw new ArgumentNullException(
                nameof(@namespace));
        }
    }
}
