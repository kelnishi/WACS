// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.Types
{
    /// <summary>
    /// A named free function inside an interface. Distinct from
    /// <see cref="CtResourceMethod"/>, which attaches to a resource.
    /// </summary>
    public sealed class CtInterfaceFunction
    {
        public string Name { get; }
        public CtFunctionType Type { get; }
        public CtInterfaceFunction(string name, CtFunctionType type)
        {
            Name = name;
            Type = type;
        }
    }

    /// <summary>
    /// A qualified package name. Mirrors the WIT AST's
    /// <c>WitPackageName</c> but lives in the Types layer so consumers
    /// downstream of the WIT parser don't have to hold a WIT AST
    /// reference just to identify which interface they're looking at.
    /// </summary>
    public sealed class CtPackageName
    {
        public string Namespace { get; }
        public IReadOnlyList<string> Path { get; }
        public string? Version { get; }

        public CtPackageName(string ns, IReadOnlyList<string> path, string? version)
        {
            Namespace = ns;
            Path = path;
            Version = version;
        }

        /// <summary>
        /// Canonical textual form: <c>ns:p1/p2@ver</c>. Used as an
        /// interface-lookup key and for matching against import names in
        /// wasm modules (<c>wasi:io/streams@0.2.3</c>).
        /// </summary>
        public override string ToString()
        {
            var path = string.Join(":", Path);
            var ver = Version != null ? "@" + Version : "";
            return Namespace + ":" + path + ver;
        }
    }

    /// <summary>
    /// An interface — a named bundle of type definitions and functions.
    /// Both world imports and world exports reference interfaces by
    /// name; the interface is the reusable unit of component-model type
    /// structure.
    /// </summary>
    public sealed class CtInterfaceType
    {
        /// <summary>
        /// The owning package name, when the interface lives inside a
        /// package declaration. Null for inline interfaces declared
        /// directly in a world (e.g., <c>import foo: interface { … }</c>).
        /// </summary>
        public CtPackageName? Package { get; }

        /// <summary>The interface's local name (e.g., <c>streams</c>).</summary>
        public string Name { get; }

        /// <summary>
        /// Type definitions declared inside the interface. Order matches
        /// declaration order in the WIT source — downstream consumers
        /// (C# emitter, canonical ABI) sometimes care about it.
        /// </summary>
        public IReadOnlyList<CtNamedType> Types { get; }

        /// <summary>Free functions (not on resources) declared inside the interface.</summary>
        public IReadOnlyList<CtInterfaceFunction> Functions { get; }

        /// <summary>
        /// Types this interface imports via <c>use</c> statements. Each
        /// entry is a (path, used-name, optional-alias) triple.
        /// </summary>
        public IReadOnlyList<CtUse> Uses { get; }

        public CtInterfaceType(
            CtPackageName? package,
            string name,
            IReadOnlyList<CtNamedType> types,
            IReadOnlyList<CtInterfaceFunction> functions,
            IReadOnlyList<CtUse> uses)
        {
            Package = package;
            Name = name;
            Types = types;
            Functions = functions;
            Uses = uses;
        }

        /// <summary>
        /// Canonical textual form: <c>wasi:io/streams@0.2.3</c>. Matches
        /// the namespace string wit-bindgen and wasm import decls use.
        /// </summary>
        public string QualifiedName =>
            Package == null ? Name : Package.ToString() + "/" + Name;
    }

    /// <summary>
    /// A <c>use</c> import inside an interface or world, like
    /// <c>use wasi:io/poll@0.2.3.{pollable};</c>. Resolution (binding
    /// each <see cref="CtUsedName"/> to a concrete <see cref="CtNamedType"/>)
    /// happens in a later pass once all interfaces have been enumerated.
    /// </summary>
    public sealed class CtUse
    {
        public CtPackageName? Package { get; }
        public string InterfaceName { get; }
        public IReadOnlyList<CtUsedName> Names { get; }

        public CtUse(CtPackageName? package, string interfaceName,
                     IReadOnlyList<CtUsedName> names)
        {
            Package = package;
            InterfaceName = interfaceName;
            Names = names;
        }
    }

    /// <summary>A single imported name from a <c>use</c> list, optionally aliased.</summary>
    public sealed class CtUsedName
    {
        public string Name { get; }
        public string? Alias { get; }
        public CtUsedName(string name, string? alias) { Name = name; Alias = alias; }

        /// <summary>The name the interface sees — alias if present, else the original name.</summary>
        public string LocalName => Alias ?? Name;
    }
}
