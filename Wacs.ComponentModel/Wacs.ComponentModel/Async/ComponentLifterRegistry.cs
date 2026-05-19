// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Process-wide table of typed value lifters, keyed by WIT
    /// identifier. Populated at startup by per-assembly
    /// <c>[ModuleInitializer]</c>-decorated methods the
    /// <c>WACS.ComponentModel.Async.SourceGen.ComponentLifterRegistryGenerator</c>
    /// emits — one initializer per assembly that contains at
    /// least one <see cref="ComponentLifterAttribute"/>-decorated
    /// static method.
    ///
    /// <para>Consumers pair the registry with
    /// <see cref="AsyncDispatcher.RegisterTypeMapping(uint, string)"/>:
    /// at instantiation time, bindgen-emitted code (or the embedder)
    /// declares "type-table index N is WIT identifier X"; at lift
    /// time the binder asks
    /// <see cref="AsyncDispatcher.TryGetTypedLifterForTypeIdx(uint, out System.Delegate?)"/>
    /// which routes through this registry.</para>
    ///
    /// <para><b>No runtime reflection.</b> The generator emits
    /// each <see cref="Register(string, Delegate)"/> call literally;
    /// the registry's storage is a plain dictionary.</para>
    ///
    /// <para><b>Thread-safety.</b>
    /// <see cref="Register(string, Delegate)"/> is intended for
    /// module-initializer use — call sites are emitted by the
    /// source generator, run once per assembly on load, single-
    /// threaded. After startup the table is treated as read-only
    /// (lookups are lock-free reads against the dictionary).</para>
    /// </summary>
    public static class ComponentLifterRegistry
    {
        private static readonly Dictionary<string, Delegate> _table =
            new Dictionary<string, Delegate>(StringComparer.Ordinal);

        /// <summary>
        /// Register a typed lifter for <paramref name="witIdent"/>.
        /// Idempotent for the same (ident, delegate) pair. Throws
        /// when a different delegate is registered for an
        /// already-known ident — surfaces a bindgen / mis-merge bug
        /// rather than silently overwriting.
        ///
        /// <para>Source-generator-emitted call sites; not intended
        /// for runtime use by application code after startup.</para>
        /// </summary>
        public static void Register(string witIdent, Delegate lifter)
        {
            if (witIdent == null) throw new ArgumentNullException(nameof(witIdent));
            if (lifter == null) throw new ArgumentNullException(nameof(lifter));
            if (_table.TryGetValue(witIdent, out var existing))
            {
                if (!ReferenceEquals(existing, lifter)
                    && existing.Method != lifter.Method)
                {
                    throw new InvalidOperationException(
                        $"WIT identifier '{witIdent}' is already " +
                        $"registered to a different lifter " +
                        $"({existing.Method.DeclaringType?.FullName}." +
                        $"{existing.Method.Name}); refusing to " +
                        $"overwrite with " +
                        $"{lifter.Method.DeclaringType?.FullName}." +
                        $"{lifter.Method.Name}.");
                }
                return;
            }
            _table[witIdent] = lifter;
        }

        /// <summary>
        /// Try to resolve a typed lifter for the given WIT
        /// identifier. Returns false (and a null
        /// <paramref name="lifter"/>) when no
        /// <see cref="ComponentLifterAttribute"/>-marked method
        /// targets that ident.
        /// </summary>
        public static bool TryGet(string witIdent, out Delegate? lifter)
        {
            if (witIdent == null)
                throw new ArgumentNullException(nameof(witIdent));
            if (_table.TryGetValue(witIdent, out var d))
            {
                lifter = d;
                return true;
            }
            lifter = null;
            return false;
        }

        /// <summary>All registered WIT identifiers, snapshot at the
        /// time of the call.</summary>
        public static IReadOnlyCollection<string> Idents
        {
            get
            {
                var snapshot = new List<string>(_table.Count);
                snapshot.AddRange(_table.Keys);
                return snapshot;
            }
        }

        /// <summary>Count of registered typed lifters.</summary>
        public static int Count => _table.Count;
    }
}
