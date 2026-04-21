// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

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
        /// Pre-scan: register a name at its anticipated pass-2 index. Pass 2
        /// later walks in the same order and consumes these entries; its
        /// <see cref="Declare(string)"/> calls return the pre-registered
        /// index (verified for consistency) without double-mapping.
        /// </summary>
        public void PrereserveName(string name, int index)
        {
            if (_byName.ContainsKey(name))
                throw new System.FormatException($"duplicate name {name} in namespace");
            _byName[name] = index;
        }

        /// <summary>
        /// Pass-2 declaration. Advances <see cref="Count"/>; if the name was
        /// pre-registered, verifies the assigned index matches (same
        /// traversal order). Anonymous entries (null/empty name) just
        /// advance.
        /// </summary>
        public int Declare(string? name)
        {
            int idx = Count++;
            if (!string.IsNullOrEmpty(name))
            {
                if (_byName.TryGetValue(name!, out var pre))
                {
                    if (pre != idx)
                        throw new System.InvalidOperationException(
                            $"name table drift: {name} pre-registered at {pre}, pass 2 at {idx}");
                }
                else
                {
                    _byName[name!] = idx;
                }
            }
            return idx;
        }

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

        /// <summary>
        /// Parallel to Module.Types — true if the entry came from a
        /// <c>(rec …)</c> wrapper, false if it's a plain <c>(type …)</c> or
        /// an inline-typeuse synthesis. Used for dedup discipline:
        /// rec-grouped entries are NOT matched by inline-typeuse
        /// synthesis, matching the binary encoder's behavior.
        /// </summary>
        public List<bool> TypesFromRec { get; } = new List<bool>();
    }

    /// <summary>
    /// Function-scope parse state, created per-function body. Tracks locals
    /// (indexed over params + declared locals) and the label stack as we
    /// descend into block / loop / if forms.
    /// </summary>
    internal sealed class TextFunctionContext
    {
        public TextParseContext Module { get; }
        public List<string?> LocalNames { get; } = new List<string?>();
        public List<ValType> LocalTypes { get; } = new List<ValType>();
        /// <summary>
        /// Label stack. Top of stack = innermost block. Empty string for
        /// anonymous blocks. Resolution from a <c>br $name</c> counts from
        /// the top (depth 0 = innermost).
        /// </summary>
        public List<string?> LabelStack { get; } = new List<string?>();

        public TextFunctionContext(TextParseContext module) { Module = module; }

        public bool TryResolveLocal(string name, out int idx)
        {
            for (int i = LocalNames.Count - 1; i >= 0; i--)
            {
                if (LocalNames[i] == name) { idx = i; return true; }
            }
            idx = -1;
            return false;
        }

        public bool TryResolveLabel(string name, out int depth)
        {
            // Top of stack = innermost = depth 0. Scan backwards.
            for (int i = LabelStack.Count - 1; i >= 0; i--)
            {
                if (LabelStack[i] == name) { depth = LabelStack.Count - 1 - i; return true; }
            }
            depth = -1;
            return false;
        }
    }
}
