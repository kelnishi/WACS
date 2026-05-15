// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Runtime support helpers consumed by source-gen-emitted
    /// host-interface code. Centralized so the generator can
    /// reference one well-known type rather than embedding
    /// reflection boilerplate in every generated source.
    /// </summary>
    public static class HostInterfaceRuntime
    {
        // (interfaceType, methodName) -> MethodInfo cache.
        // Tested + invoked in a hot path: every `[static]X.foo`
        // direct-link call hits this lookup once at first call,
        // then resolves from cache.
        private static readonly ConcurrentDictionary<(Type, string), MethodInfo?>
            _staticFactoryCache = new ConcurrentDictionary<(Type, string), MethodInfo?>();

        /// <summary>
        /// Generated default-static-interface bodies for WIT
        /// <c>[static]X.foo</c> methods call this to dispatch to
        /// the concrete impl class's <c>FooStatic</c> factory.
        /// Walks loaded assemblies for a non-abstract class
        /// implementing <paramref name="ifaceType"/> with a
        /// public static method named
        /// <paramref name="methodName"/>; returns its boxed
        /// result (or null for void factories). Caller casts
        /// to the WIT-declared return type — codegen knows it
        /// at emit time.
        /// </summary>
        public static object? InvokeStaticFactoryReflective(
            Type ifaceType, string methodName, object?[] args)
        {
            var mi = _staticFactoryCache.GetOrAdd(
                (ifaceType, methodName),
                key => FindStaticFactory(key.Item1, key.Item2));
            if (mi == null)
                throw new InvalidOperationException(
                    "No static factory `" + methodName
                    + "` found on any class implementing "
                    + ifaceType.FullName + ". Add a `public static T "
                    + methodName + "(...)` to your impl class.");
            return mi.Invoke(null, args);
        }

        private static MethodInfo? FindStaticFactory(
            Type ifaceType, string methodName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsInterface || t.IsAbstract) continue;
                    if (!ifaceType.IsAssignableFrom(t)) continue;
                    var mi = t.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.Static);
                    if (mi != null) return mi;
                }
            }
            return null;
        }
    }
}
