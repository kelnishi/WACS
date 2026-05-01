// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.Types
{
    /// <summary>
    /// A world — the top-level contract a component binds to. A world
    /// declares a set of imports (host-provided functionality) and
    /// exports (component-provided functionality). Each entry references
    /// either an interface, an inline function, or an inline interface.
    ///
    /// <para>Worlds are the component-model analog of an executable's
    /// "linker input": when a host instantiates a component, it must
    /// supply implementations of every import in the component's
    /// declared world, and can invoke every export in return.</para>
    /// </summary>
    public sealed class CtWorldType
    {
        public CtPackageName? Package { get; }
        public string Name { get; }

        /// <summary>Type definitions declared at world-level (outside any interface).</summary>
        public IReadOnlyList<CtNamedType> Types { get; }

        /// <summary>Types this world imports via <c>use</c> statements at world scope.</summary>
        public IReadOnlyList<CtUse> Uses { get; }

        public IReadOnlyList<CtWorldImport> Imports { get; }
        public IReadOnlyList<CtWorldExport> Exports { get; }

        /// <summary>
        /// Included worlds — <c>include foo;</c> splices another world's
        /// imports/exports into this one. Kept as unresolved references
        /// here; expansion happens alongside <c>use</c> resolution.
        /// </summary>
        public IReadOnlyList<CtWorldInclude> Includes { get; }

        public CtWorldType(
            CtPackageName? package,
            string name,
            IReadOnlyList<CtNamedType> types,
            IReadOnlyList<CtUse> uses,
            IReadOnlyList<CtWorldImport> imports,
            IReadOnlyList<CtWorldExport> exports,
            IReadOnlyList<CtWorldInclude> includes)
        {
            Package = package;
            Name = name;
            Types = types;
            Uses = uses;
            Imports = imports;
            Exports = exports;
            Includes = includes;
        }

        /// <summary>Canonical textual form: <c>wasi:cli/command@0.2.3</c>.</summary>
        public string QualifiedName =>
            Package == null ? Name : Package.ToString() + "/" + Name;
    }

    /// <summary>
    /// A world import or export. Both carry the same spec-shape
    /// (a name + what's being imported/exported), distinguished only
    /// by direction.
    /// </summary>
    public abstract class CtWorldPort
    {
        /// <summary>
        /// The name under which the import/export is bound. Named
        /// imports take the local name (e.g. <c>environment</c>);
        /// interface-referenced imports take the full qualified name
        /// (e.g. <c>wasi:cli/environment</c>).
        /// </summary>
        public string Name { get; }
        public CtExternType Spec { get; }
        protected CtWorldPort(string name, CtExternType spec)
        {
            Name = name;
            Spec = spec;
        }
    }

    /// <summary>A world-level <c>import name: spec;</c>.</summary>
    public sealed class CtWorldImport : CtWorldPort
    {
        public CtWorldImport(string name, CtExternType spec) : base(name, spec) { }
    }

    /// <summary>A world-level <c>export name: spec;</c>.</summary>
    public sealed class CtWorldExport : CtWorldPort
    {
        public CtWorldExport(string name, CtExternType spec) : base(name, spec) { }
    }

    /// <summary>
    /// A <c>include other-world;</c> directive. Spec expansion is
    /// deferred to resolution — we hold the symbolic reference here.
    /// </summary>
    public sealed class CtWorldInclude
    {
        public CtPackageName? Package { get; }
        public string WorldName { get; }
        public CtWorldInclude(CtPackageName? package, string worldName)
        {
            Package = package;
            WorldName = worldName;
        }
    }

    // ---- Extern types ----------------------------------------------------

    /// <summary>
    /// The "what" of an import or export — the three spec shapes are an
    /// interface reference, an inline function, or an inline interface
    /// body.
    /// </summary>
    public abstract class CtExternType
    {
    }

    /// <summary>
    /// <c>name: pkg:ns/iface@ver</c> — reference to a named interface.
    /// The target interface binding is filled in during resolution;
    /// freshly-converted values hold the symbolic path only.
    /// </summary>
    public sealed class CtExternInterfaceRef : CtExternType
    {
        public CtPackageName? Package { get; }
        public string InterfaceName { get; }

        /// <summary>Resolved target — null until the resolver binds it.</summary>
        public CtInterfaceType? Target { get; set; }

        public CtExternInterfaceRef(CtPackageName? package, string interfaceName)
        {
            Package = package;
            InterfaceName = interfaceName;
        }
    }

    /// <summary><c>name: func(params) -&gt; result</c> — inline function.</summary>
    public sealed class CtExternFunc : CtExternType
    {
        public CtFunctionType Function { get; }
        public CtExternFunc(CtFunctionType function) { Function = function; }
    }

    /// <summary><c>name: interface { … }</c> — inline interface body.</summary>
    public sealed class CtExternInlineInterface : CtExternType
    {
        public CtInterfaceType Interface { get; }
        public CtExternInlineInterface(CtInterfaceType iface) { Interface = iface; }
    }
}
