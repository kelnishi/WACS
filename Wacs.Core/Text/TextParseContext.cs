// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Name-index tables, tracked per namespace. WAT allows a <c>$name</c> to
    /// be bound to each entity at declaration time; references elsewhere in
    /// the module (index operands in instructions, <c>(export … (func $f))</c>,
    /// etc.) resolve against these tables.
    ///
    /// Spec 6.3.5.1 defines eight disjoint namespaces (types, funcs, tables,
    /// mems, globals, elem, data, locals — plus labels within function scope).
    /// Tags get their own namespace under the exception-handling proposal.
    /// </summary>
    internal sealed class NameTable
    {
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(System.StringComparer.Ordinal);
        public int Count { get; private set; }

        /// <summary>
        /// Register a new declaration. <paramref name="name"/> may be null/
        /// empty for anonymous entries; named entries must not collide.
        /// Returns the assigned index.
        /// </summary>
        public int Declare(string? name)
        {
            int idx = Count++;
            if (!string.IsNullOrEmpty(name))
            {
                if (_byName.ContainsKey(name!))
                    throw new System.FormatException($"duplicate name {name} in namespace");
                _byName[name!] = idx;
            }
            return idx;
        }

        /// <summary>
        /// Reserve the next index slot without binding a name. Used when a
        /// section-level form defers naming (e.g. inline-imported entities
        /// still need a slot in their namespace).
        /// </summary>
        public int ReserveAnonymous() => Count++;

        public bool TryResolve(string name, out int idx) => _byName.TryGetValue(name, out idx);
    }

    /// <summary>
    /// Parse state passed through all section / form parsers. Carries the
    /// <see cref="Module"/> being built, per-namespace name tables, and the
    /// root-level s-expression stream.
    /// </summary>
    internal sealed class TextParseContext
    {
        public Module Module { get; } = new Module();

        public NameTable Types    { get; } = new NameTable();
        public NameTable Funcs    { get; } = new NameTable();
        public NameTable Tables   { get; } = new NameTable();
        public NameTable Mems     { get; } = new NameTable();
        public NameTable Globals  { get; } = new NameTable();
        public NameTable Elems    { get; } = new NameTable();
        public NameTable Datas    { get; } = new NameTable();
        public NameTable Tags     { get; } = new NameTable();

        // Synthetic function types generated from inline typeuse abbreviations
        // — Phase 1.4 may push into this as it walks func signatures with no
        // explicit (type $x) reference. Keeps the list on hand so TypeSection
        // order stays stable.
        public List<FunctionType> SyntheticTypes { get; } = new List<FunctionType>();
    }
}
