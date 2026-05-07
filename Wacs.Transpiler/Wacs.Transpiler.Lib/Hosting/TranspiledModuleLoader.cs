// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Wacs.Transpiler.Hosting
{
    /// <summary>
    /// Public entry point for loading a transpiled WASM module from a saved
    /// <c>.dll</c>. Discovers the generated <c>Module</c> class, the
    /// <c>IExports</c> / <c>IImports</c> interfaces, and the embedded init-
    /// data resource; handles import wiring through an explicit, typed path
    /// (caller supplies an object implementing the generated
    /// <c>IImports</c>) or a by-name dispatch path (caller supplies a
    /// dictionary of delegates keyed by WASM import name).
    ///
    /// <para>Both paths expose the loaded interface types as first-class
    /// reflection objects, so consumers can interrogate the module's shape
    /// (<see cref="LoadedModule.ExportsInterface"/>,
    /// <see cref="LoadedModule.ImportsInterface"/>) before binding or
    /// invocation.</para>
    ///
    /// <para>Non-AOT only. The loader uses <see cref="DispatchProxy"/> for
    /// by-name import binding, which is incompatible with strict AOT
    /// trimming. The typed path (caller-provided object) stays AOT-safe.</para>
    /// </summary>
    public static class TranspiledModuleLoader
    {
        // =================================================================
        // Public entry points
        // =================================================================

        /// <summary>
        /// Load a transpiled <c>.dll</c> from disk into a fresh
        /// <see cref="AssemblyLoadContext"/> and prepare the Module class
        /// for invocation. Use <paramref name="imports"/> when the module
        /// has imports (see <see cref="LoadedModule.ImportsInterface"/>).
        /// </summary>
        /// <param name="path">Path to the saved <c>.dll</c>.</param>
        /// <param name="imports">
        /// Either (a) an object implementing the generated
        /// <c>IImports</c> type, or (b) an <c>IDictionary&lt;string,
        /// Delegate&gt;</c> keyed by WASM import name (dispatched via
        /// <see cref="DispatchProxy"/>), or (c) null if the module has
        /// no imports.
        /// </param>
        /// <param name="isolate">When true, load into a fresh collectible
        /// <see cref="AssemblyLoadContext"/>. When false, load into the
        /// default context — useful when the caller wants the assembly's
        /// types to be referenceable from code outside the loader.</param>
        public static LoadedModule Load(string path, object? imports = null, bool isolate = true)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Transpiled assembly not found", path);

            // DispatchProxy builds its proxy in a non-collectible assembly,
            // which can't legally reference types from a collectible one.
            // When the caller asks for by-name delegate dispatch, auto-
            // downgrade isolation so DispatchProxy can build against the
            // loaded IImports. Typed-object imports don't hit DispatchProxy
            // and keep full isolation.
            bool needsDispatchProxy = imports is IDictionary<string, Delegate>;
            bool effectiveIsolate = isolate && !needsDispatchProxy;

            Assembly asm;
            if (effectiveIsolate)
            {
                var ctx = new AssemblyLoadContext(
                    "wacs-transpiled-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                    isCollectible: true);
                using var stream = File.OpenRead(path);
                asm = ctx.LoadFromStream(stream);
            }
            else
            {
                asm = Assembly.LoadFrom(path);
            }

            return Load(asm, imports);
        }

        /// <summary>
        /// Load a transpiled module from an already-loaded
        /// <see cref="Assembly"/> (e.g. when the caller holds a reference
        /// to a dynamic assembly produced in-process by
        /// <see cref="AOT.ModuleTranspiler"/>).
        /// </summary>
        public static LoadedModule Load(Assembly assembly, object? imports = null)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var moduleType = FindModuleClass(assembly)
                ?? throw new InvalidOperationException(
                    "TranspiledModuleLoader: no Module class found in assembly. " +
                    "Was this assembly produced by Wacs.Transpiler?");

            var exportsIface = FindInterface(moduleType, "IExports");
            var importsIface = FindImportsInterface(moduleType);

            object? importsArg = ResolveImports(importsIface, imports);

            object instance;
            try
            {
                instance = importsArg == null
                    ? Activator.CreateInstance(moduleType)!
                    : Activator.CreateInstance(moduleType, importsArg)!;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }

            return new LoadedModule(assembly, moduleType, exportsIface, importsIface, instance);
        }

        // =================================================================
        // Discovery
        // =================================================================

        private static Type? FindModuleClass(Assembly assembly)
        {
            // Module class emitted by ModuleClassGenerator as
            // "{Namespace}.{ModuleName}.Module". The outer namespace is
            // consumer-chosen and ModuleName is by default "WasmModule" —
            // both can vary per transpile, so match by structure: a public
            // type named "Module" that has the "_ctx" private field the
            // transpiler always emits.
            foreach (var t in assembly.GetExportedTypes())
            {
                if (t.Name != "Module") continue;
                var ctxField = t.GetField("_ctx",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctxField != null) return t;
            }
            return null;
        }

        private static Type? FindInterface(Type moduleType, string leafName)
        {
            // IExports is implemented directly by Module; IImports is the
            // ctor's parameter type. Both live in Module's namespace
            // (ModuleName prefix handled implicitly by interface-gen).
            foreach (var iface in moduleType.GetInterfaces())
                if (iface.Name == leafName) return iface;
            return null;
        }

        private static Type? FindImportsInterface(Type moduleType)
        {
            // The Module ctor takes IImports when the module has imports,
            // no parameters otherwise. Scan public ctors and pick the one
            // whose sole parameter type's name is "IImports".
            foreach (var ctor in moduleType.GetConstructors())
            {
                var ps = ctor.GetParameters();
                if (ps.Length != 1) continue;
                if (ps[0].ParameterType.Name == "IImports") return ps[0].ParameterType;
            }
            return null;
        }

        // =================================================================
        // Import resolution
        // =================================================================

        private static object? ResolveImports(Type? importsIface, object? provided)
        {
            if (importsIface == null) return null;

            if (provided == null)
                throw new InvalidOperationException(
                    $"TranspiledModuleLoader: module requires imports (expects {importsIface.FullName}), but none were provided.");

            // Case 1: caller already built an implementation of the
            // generated interface. Pass through verbatim. AOT-safe.
            if (importsIface.IsInstanceOfType(provided)) return provided;

            // Case 2: caller supplied a dict keyed by WASM import name.
            // Wrap via DispatchProxy. Requires reflection-based dispatch —
            // not AOT-safe; documented as the non-AOT path.
            if (provided is IDictionary<string, Delegate> byName)
                return BuildDispatchProxy(importsIface, byName);

            throw new ArgumentException(
                $"TranspiledModuleLoader: imports argument of type {provided.GetType().FullName} " +
                $"neither implements {importsIface.FullName} nor is an IDictionary<string, Delegate>.",
                nameof(provided));
        }

        private static object BuildDispatchProxy(Type importsIface, IDictionary<string, Delegate> byName)
        {
            // DispatchProxy.Create<T, TProxy> requires a compile-time generic
            // for T — but here T is known only at runtime. Fall back to the
            // static Create(interfaceType, proxyType) overload.
            var proxy = DispatchProxy.Create(importsIface, typeof(DelegateDispatcher));
            ((DelegateDispatcher)(object)proxy).Handlers = byName;
            return proxy;
        }

        /// <summary>
        /// <see cref="DispatchProxy"/> implementation that forwards each
        /// generated-interface method call to a <see cref="Delegate"/> in
        /// a name-keyed dictionary. The key is the method's CLR name, which
        /// matches the sanitized WASM import name used by the interface
        /// generator (<c>InterfaceGenerator.SanitizeName(module_name)</c>).
        /// </summary>
        public class DelegateDispatcher : DispatchProxy
        {
            public IDictionary<string, Delegate> Handlers { get; set; } = new Dictionary<string, Delegate>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod == null) return null;
                if (!Handlers.TryGetValue(targetMethod.Name, out var del))
                    throw new MissingMethodException(
                        $"TranspiledModuleLoader: no delegate registered for import '{targetMethod.Name}' " +
                        "(expected a key matching the sanitized WASM import name).");
                return del.DynamicInvoke(args);
            }
        }
    }

    /// <summary>
    /// Handle to a loaded transpiled module. Exposes reflection-first
    /// metadata so consumers can enumerate exports / imports and build
    /// typed wrappers, plus direct <see cref="Invoke"/> for by-name calls
    /// that don't need a typed interface at the call site.
    /// </summary>
    public sealed class LoadedModule
    {
        public Assembly Assembly { get; }
        public Type ModuleType { get; }
        public Type? ExportsInterface { get; }
        public Type? ImportsInterface { get; }
        /// <summary>The constructed module instance. Safe to cast to
        /// <see cref="ExportsInterface"/> (if non-null).</summary>
        public object Instance { get; }

        internal LoadedModule(Assembly asm, Type moduleType, Type? exportsIface, Type? importsIface, object instance)
        {
            Assembly = asm;
            ModuleType = moduleType;
            ExportsInterface = exportsIface;
            ImportsInterface = importsIface;
            Instance = instance;
        }

        /// <summary>
        /// Enumerate export methods as <see cref="MethodInfo"/>s on the
        /// generated <see cref="ExportsInterface"/>. Consumers that know
        /// an export's shape at compile time should prefer
        /// <see cref="GetExport{TDelegate}"/> for cheap invocation; this
        /// enumerator is for discovery / tooling.
        /// </summary>
        public IEnumerable<MethodInfo> Exports
        {
            get
            {
                if (ExportsInterface == null) yield break;
                foreach (var m in ExportsInterface.GetMethods()) yield return m;
            }
        }

        /// <summary>
        /// Invoke an export by name, returning its return value
        /// (<c>null</c> for void exports). Name matches the export's CLR
        /// name on the generated interface — which the interface generator
        /// sanitizes from the WASM export name (same sanitizer the Module
        /// class's method names use, so both spellings work for identifier-
        /// valid WASM names).
        /// </summary>
        public object? Invoke(string exportName, params object?[] args)
        {
            if (exportName == null) throw new ArgumentNullException(nameof(exportName));
            var method = ModuleType.GetMethod(exportName,
                BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                throw new MissingMethodException(
                    $"Export '{exportName}' not found on {ModuleType.FullName}. Available: " +
                    string.Join(", ", Exports.Select(m => "\"" + m.Name + "\"")));
            try
            {
                return method.Invoke(Instance, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        /// <summary>
        /// Build a typed delegate bound to the named export. Fails if
        /// the delegate signature doesn't match the export's signature.
        /// Faster and cleaner at the call site than
        /// <see cref="Invoke"/> for known-shape exports.
        /// </summary>
        public TDelegate GetExport<TDelegate>(string exportName) where TDelegate : Delegate
        {
            if (exportName == null) throw new ArgumentNullException(nameof(exportName));
            var method = ModuleType.GetMethod(exportName,
                BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                throw new MissingMethodException(
                    $"Export '{exportName}' not found on {ModuleType.FullName}.");
            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), Instance, method);
        }
    }
}
