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
    /// Build-time-resolved set of canon-async op names the
    /// <see cref="AsyncDispatcher"/> handles — derived from the
    /// <see cref="CanonAsyncAttribute"/>-decorated methods on
    /// the dispatcher at compile time by the
    /// <c>WACS.ComponentModel.Async.SourceGen</c> Roslyn source
    /// generator.
    ///
    /// <para>The shim-module recognizer (future slice) consumes
    /// this to validate that a debug-name from a wit-component-
    /// emitted <c>"wit-component:shim"</c> module's function-name
    /// custom section (after dot→dash normalization) corresponds
    /// to a canon op WACS knows how to dispatch. The actual
    /// delegate construction goes through
    /// <see cref="CanonAsyncBinder"/>'s per-shape switch — this
    /// registry is the name-validation surface.</para>
    ///
    /// <para><b>No runtime reflection.</b> The generator emits
    /// <see cref="BuildKnownNames"/> as a static partial method
    /// that returns the literal set. The hand-written portion
    /// of this class only exposes the lookup API; the data is
    /// build-time.</para>
    /// </summary>
    public static partial class CanonOpRegistry
    {
        // Lazy-initialized from the generator-supplied
        // BuildKnownNames(). Initialization is allocation-free
        // once the cache is set.
        private static HashSet<string>? _cache;

        private static HashSet<string> Cache =>
            _cache ??= BuildKnownNames();

        /// <summary>
        /// True iff <paramref name="canonOpName"/> matches a
        /// <see cref="CanonAsyncAttribute"/>-decorated method on
        /// <see cref="AsyncDispatcher"/>. The expected spelling
        /// is wasmtime's <c>Trampoline::symbol_name()</c> form —
        /// kebab-case (<c>"task-return"</c>, <c>"stream-new"</c>,
        /// <c>"waitable-set-wait"</c>).
        /// </summary>
        public static bool IsKnown(string canonOpName)
        {
            if (canonOpName == null)
                throw new ArgumentNullException(nameof(canonOpName));
            return Cache.Contains(canonOpName);
        }

        /// <summary>All registered canon-op names, sorted.</summary>
        public static IReadOnlyCollection<string> Names => Cache;

        /// <summary>Count of registered canon ops. Source
        /// generator emits the literal value via the partial; the
        /// count is a quick sanity check for tests.</summary>
        public static int Count => Cache.Count;

        // Supplied by the source generator. The hand-written
        // declaration here is the contract; the implementation
        // lives in CanonOpRegistry.g.cs.
        private static partial HashSet<string> BuildKnownNames();
    }
}
