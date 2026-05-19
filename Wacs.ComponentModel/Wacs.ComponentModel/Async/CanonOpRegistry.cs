// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Canon-op-name → <see cref="MethodInfo"/> registry for the
    /// methods on <see cref="AsyncDispatcher"/> tagged with
    /// <see cref="CanonAsyncAttribute"/>. The shim-module
    /// recognizer (Slice G3) consumes this when binding
    /// wit-component-emitted components — read the shim's
    /// function-name custom section to get debug names like
    /// <c>"task.return"</c>, normalize to wasmtime kebab
    /// spelling (<c>"task-return"</c>), look up the dispatcher
    /// method here.
    ///
    /// <para><b>Slice G1 (this slice)</b>: discovery happens at
    /// first access via <see cref="System.Reflection"/>. The
    /// reflection requires runtime dynamic-code generation
    /// (<see cref="MethodInfo.Invoke"/> path), so the API is
    /// annotated with <see cref="RequiresDynamicCodeAttribute"/>
    /// and <see cref="RequiresUnreferencedCodeAttribute"/>.</para>
    ///
    /// <para><b>Slice G2</b>: a Roslyn source generator emits
    /// the same lookup table at build time. The registry's
    /// public API stays identical; the reflection path is
    /// replaced with a generated static dictionary and the
    /// AOT annotations come off.</para>
    /// </summary>
    public static class CanonOpRegistry
    {
        private static Dictionary<string, MethodInfo>? _cache;

        /// <summary>Look up the <see cref="AsyncDispatcher"/>
        /// method tagged with
        /// <c>[CanonAsync(<paramref name="canonOpName"/>)]</c>,
        /// or <c>null</c> when no method matches.</summary>
        [RequiresDynamicCode("Slice G1 uses reflection over " +
            "AsyncDispatcher's [CanonAsync] attributes. Slice G2 " +
            "replaces this with a build-time source generator.")]
        [RequiresUnreferencedCode("Reflection over methods may " +
            "remove decorated methods through trimming.")]
        public static MethodInfo? GetMethod(string canonOpName)
        {
            if (canonOpName == null)
                throw new ArgumentNullException(nameof(canonOpName));
            EnsureCache();
            return _cache!.TryGetValue(canonOpName, out var mi) ? mi : null;
        }

        /// <summary>All canon-op names registered on
        /// <see cref="AsyncDispatcher"/>.</summary>
        public static IEnumerable<string> Names
        {
            [RequiresDynamicCode("Slice G1 uses reflection (see " +
                nameof(GetMethod) + " note).")]
            [RequiresUnreferencedCode("Reflection over methods.")]
            get { EnsureCache(); return _cache!.Keys; }
        }

        /// <summary>Count of registered canon ops. Useful for
        /// the source generator's correctness check: regenerated
        /// table size must match the reflective scan's count.</summary>
        public static int Count
        {
            [RequiresDynamicCode("Slice G1 uses reflection (see " +
                nameof(GetMethod) + " note).")]
            [RequiresUnreferencedCode("Reflection over methods.")]
            get { EnsureCache(); return _cache!.Count; }
        }

        [RequiresDynamicCode("Reflection scan over " +
            nameof(AsyncDispatcher) + ".")]
        [RequiresUnreferencedCode("Reflection over methods.")]
        private static void EnsureCache()
        {
            if (_cache != null) return;
            var map = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            var dispatcher = typeof(AsyncDispatcher);
            var methods = dispatcher.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            foreach (var m in methods)
            {
                var attr = m.GetCustomAttribute<CanonAsyncAttribute>();
                if (attr == null) continue;
                if (map.ContainsKey(attr.Name))
                    throw new InvalidOperationException(
                        $"Duplicate [CanonAsync(\"{attr.Name}\")] on " +
                        $"{dispatcher.Name}.{m.Name} (already bound to " +
                        $"{map[attr.Name].Name}).");
                map[attr.Name] = m;
            }
            _cache = map;
        }
    }
}
