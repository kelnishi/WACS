// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Wacs.Core.Runtime;

namespace Wacs.Transpiler.Hosting
{
    /// <summary>
    /// Loads host-binding assemblies (libraries that expose types
    /// implementing <see cref="IBindable"/>) and activates each
    /// discovered binding with a parameterless constructor. Used by
    /// CLI tools so they can accept any WACS-compatible host library
    /// — WASI, a game engine host, a custom syscall shim — without
    /// hard-coding each one in.
    /// </summary>
    public static class BindingLoader
    {
        /// <summary>
        /// Load the assembly identified by <paramref name="nameOrPath"/>,
        /// find every concrete <see cref="IBindable"/> with a
        /// parameterless constructor, instantiate it, and return the
        /// bindings. Caller is responsible for calling
        /// <see cref="IBindable.BindToRuntime"/> and (for IDisposable
        /// bindings) disposing at shutdown.
        ///
        /// <para>Resolution mirrors <c>ResolveHostPackages</c>: a
        /// file path on disk is loaded via
        /// <see cref="Assembly.LoadFrom(string)"/>; otherwise the
        /// argument is treated as an assembly name and resolved via
        /// <see cref="Assembly.Load(string)"/>. This lets
        /// <c>--bind &lt;path&gt;</c> and <c>--bind &lt;name&gt;</c>
        /// both work and matches what users expect from
        /// <c>--host-package</c>.</para>
        /// </summary>
        public static List<IBindable> LoadFromAssembly(string nameOrPath)
            => LoadFromAssembly(LoadAssembly(nameOrPath));

        /// <summary>
        /// Resolve the assembly identified by
        /// <paramref name="nameOrPath"/> WITHOUT activating any
        /// <see cref="IBindable"/> types. A file path on disk is
        /// loaded via <see cref="Assembly.LoadFrom(string)"/>;
        /// otherwise the argument is treated as an assembly name
        /// and resolved via <see cref="Assembly.Load(string)"/>.
        ///
        /// <para>Used by host CLIs that need the assembly in
        /// AppDomain before triggering scope-construction-time
        /// auto-discovery (gap 26: the wasi-nn DI scope's
        /// reflective backend auto-wire walks AppDomain on miss,
        /// but requires `--bind` paths to have been
        /// `Assembly.LoadFrom`'d first). Callers wanting both
        /// load and activate use <see cref="LoadFromAssembly(string)"/>;
        /// the load step is idempotent so calling both with the
        /// same path is fine.</para>
        /// </summary>
        public static Assembly LoadAssembly(string nameOrPath)
        {
            if (File.Exists(nameOrPath))
            {
                var fullPath = Path.GetFullPath(nameOrPath);
                // Memoize by absolute path so repeat calls (e.g.,
                // RunHandler's load-then-bind double-pass: preload
                // for AppDomain visibility, then ApplyBindings
                // re-resolves) return the same Assembly instance —
                // a fresh AssemblyLoadContext per call would yield
                // distinct Type identities and break IBindable
                // matching against the host-loaded interface.
                var asm = _bindContexts.GetOrAdd(fullPath, static p =>
                {
                    var ctx = new BindBackendLoadContext(p);
                    return ctx.LoadFromAssemblyPath(p);
                });
                // Per-assembly resolver is still useful when the
                // bind asm itself has direct [DllImport] (rare, but
                // costs nothing to register). LoadUnmanagedDll on
                // BindBackendLoadContext is the catch-all for every
                // DllImport from any assembly in the context —
                // including transitive deps like TorchSharp.dll
                // (gap 30 / round-25-verification).
                RegisterNativeResolver(asm, fullPath);
                return asm;
            }
            Assembly nameAsm;
            try { nameAsm = Assembly.Load(nameOrPath); }
            catch (Exception ex)
            {
                throw new FileNotFoundException(
                    "binding assembly not found as file or name: "
                    + nameOrPath + " (" + ex.Message + ")");
            }
            // Native-lib resolver also helps for assemblies
            // resolved by name when their location is a real
            // file path (e.g., side-by-side install). Name-loaded
            // assemblies live in the default context, so the
            // LoadContext-level hook isn't available — the
            // per-assembly resolver is the best we can do.
            if (!string.IsNullOrEmpty(nameAsm.Location)
                && File.Exists(nameAsm.Location))
                RegisterNativeResolver(nameAsm, nameAsm.Location);
            return nameAsm;
        }

        // Memoize path -> Assembly so repeat LoadAssembly calls on
        // the same path yield the same Assembly instance (same
        // Type identities). Without this, the second LoadAssembly
        // would create a new BindBackendLoadContext, load the
        // assembly afresh, and IBindable types from the two loads
        // wouldn't match the host's IBindable interface in a
        // type-equality check.
        private static readonly ConcurrentDictionary<string, Assembly>
            _bindContexts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Custom <see cref="AssemblyLoadContext"/> for backends
        /// loaded via <c>--bind &lt;path&gt;</c>. The
        /// <see cref="LoadUnmanagedDll"/> override probes the
        /// backend's own <c>runtimes/&lt;rid&gt;/native/</c>
        /// subdirectory for every P/Invoke from any assembly in
        /// the context (the bind asm itself, the upstream NuGet
        /// wrapper like <c>TorchSharp.dll</c>, and any deeper
        /// transitive deps), then falls back to the default
        /// resolver. This is the load-context-level hook that
        /// gap 30 / round-25-verification identified as the
        /// missing piece — per-assembly
        /// <see cref="NativeLibrary.SetDllImportResolver"/>
        /// only fires for DllImports declared in that one
        /// assembly, but most real backends declare their
        /// <c>[DllImport]</c>s in a transitive NuGet, not the
        /// bind asm itself.
        ///
        /// <para><see cref="Load"/> defers to
        /// <see cref="AssemblyDependencyResolver"/> which honors
        /// the bind asm's <c>.deps.json</c>; managed deps not
        /// covered by the resolver fall through to the default
        /// load context (which handles host-shared assemblies
        /// like Wacs.Core, Wacs.Transpiler.Lib, etc., preserving
        /// type identity across the boundary).</para>
        /// </summary>
        private sealed class BindBackendLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _depsResolver;
            private readonly string _bindDir;
            private readonly string _rid;

            public BindBackendLoadContext(string assemblyPath)
                : base(name: "WacsBind:" + Path.GetFileName(assemblyPath),
                       isCollectible: false)
            {
                _depsResolver = new AssemblyDependencyResolver(assemblyPath);
                _bindDir = Path.GetDirectoryName(assemblyPath)!;
                _rid = RuntimeInformation.RuntimeIdentifier;
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                // Defer to the default context for assemblies the
                // host has already loaded — preserves type identity
                // across the boundary so IBindable/IBackend
                // references in the bind asm match the host's
                // copies. Without this, deps.json-driven
                // ResolveAssemblyToPath would happily return a
                // private path (since EnableDynamicLoading bundles
                // host-shared deps too) and we'd load a duplicate.
                foreach (var loaded in Default.Assemblies)
                {
                    if (loaded.GetName().Name == assemblyName.Name)
                        return null;
                }
                var path = _depsResolver.ResolveAssemblyToPath(assemblyName);
                return path != null ? LoadFromAssemblyPath(path) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                // .deps.json-driven resolution first — handles the
                // "managed library 'TorchSharp' P/Invokes
                // 'LibTorchSharp', which lives at
                // runtimes/<rid>/native/libLibTorchSharp.dylib"
                // mapping that the NuGet RID story is built around.
                var path = _depsResolver.ResolveUnmanagedDllToPath(
                    unmanagedDllName);
                if (path != null
                    && NativeLibrary.TryLoad(path, out var h1))
                    return h1;
                // Fallback: explicit probe of the bind-dir's
                // runtimes/<rid>/native/ subdirectory in case the
                // deps.json doesn't list the lib (e.g., it's a
                // dlopen target of a libtorch_cpu.dylib that .NET
                // doesn't know about, or the project doesn't ship
                // a deps.json). Walks coarser RIDs and the flat
                // bind dir last.
                var probes = new List<string>
                {
                    Path.Combine(_bindDir, "runtimes", _rid, "native"),
                };
                foreach (var coarser in CoarserRids(_rid))
                    probes.Add(Path.Combine(_bindDir, "runtimes",
                        coarser, "native"));
                probes.Add(_bindDir);
                var handle = ResolveFromProbes(unmanagedDllName, probes);
                return handle != IntPtr.Zero ? handle : IntPtr.Zero;
            }
        }

        // P/Invoke probe paths populated by EnableDynamicLoading
        // on a backend csproj. Resolved at first DllImport from
        // the LoadFrom'd assembly. Cached per Assembly so repeat
        // RegisterNativeResolver calls (e.g., load-then-bind
        // double-pass in RunHandler.PreloadBindAssemblies +
        // ApplyBindings) don't double-register the same resolver.
        private static readonly ConcurrentDictionary<Assembly, byte>
            _resolverRegistered = new();

        private static void RegisterNativeResolver(
            Assembly asm, string assemblyPath)
        {
            if (!_resolverRegistered.TryAdd(asm, 0)) return;
            var asmDir = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(asmDir)) return;

            var rid = RuntimeInformation.RuntimeIdentifier;
            var primaryProbe = Path.Combine(asmDir, "runtimes", rid, "native");
            // Fallback probes: some NuGets ship under coarser RIDs
            // (e.g., "osx" instead of "osx-arm64"; "linux" instead
            // of "linux-x64"). Walk a few candidates.
            var probes = new List<string> { primaryProbe };
            foreach (var coarser in CoarserRids(rid))
                probes.Add(Path.Combine(asmDir, "runtimes", coarser, "native"));
            // Last-ditch: the assembly's own dir (some flat-bin
            // backends ship natives next to the managed DLL).
            probes.Add(asmDir);

            try
            {
                NativeLibrary.SetDllImportResolver(asm,
                    (libraryName, callingAsm, searchPath) =>
                        ResolveFromProbes(libraryName, probes));
            }
            catch (InvalidOperationException)
            {
                // Already has a resolver registered (rare — caller
                // pre-set one before LoadAssembly). Drop the cached
                // marker so a future call site that wants to retry
                // can. Don't throw; the standard probe still works
                // for assemblies that don't ship private natives.
                _resolverRegistered.TryRemove(asm, out _);
            }
        }

        private static IntPtr ResolveFromProbes(
            string libraryName, IReadOnlyList<string> probes)
        {
            foreach (var probe in probes)
            {
                if (!Directory.Exists(probe)) continue;
                // Match the OS's typical naming convention:
                // libX.dylib (macOS), libX.so (linux), X.dll (win).
                // NativeLibrary.TryLoad's own resolution handles
                // the variant — we just point it at the probe dir.
                foreach (var candidate in CandidateFileNames(libraryName))
                {
                    var full = Path.Combine(probe, candidate);
                    if (File.Exists(full)
                        && NativeLibrary.TryLoad(full, out var handle))
                        return handle;
                }
            }
            // Returning IntPtr.Zero hands control back to the
            // standard resolver — preserves OS / TPA fallbacks.
            return IntPtr.Zero;
        }

        // The DllImport's libraryName is the user-supplied string
        // (e.g., "LibTorchSharp"). Different OSes wrap that
        // differently:
        //   macOS:   libLibTorchSharp.dylib  (lib-prefix +
        //            .dylib suffix, also bare libLibTorchSharp)
        //   Linux:   libLibTorchSharp.so
        //   Windows: LibTorchSharp.dll
        // We try a few — first match wins.
        private static IEnumerable<string> CandidateFileNames(string lib)
        {
            yield return lib;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "lib" + lib + ".dylib";
                yield return lib + ".dylib";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                yield return "lib" + lib + ".so";
                yield return lib + ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return lib + ".dll";
            }
        }

        // Some NuGets (older ORT releases, some custom builds)
        // ship under coarser RID buckets — `osx` rather than
        // `osx-arm64`, `linux` rather than `linux-x64`. Walk a
        // pragmatic list of fallbacks per platform.
        private static IEnumerable<string> CoarserRids(string rid)
        {
            // rid patterns: <os>-<arch>, e.g. "osx-arm64", "linux-x64".
            // The OS-only form is the universal coarser bucket.
            int dash = rid.IndexOf('-');
            if (dash > 0)
                yield return rid.Substring(0, dash);
        }

        /// <summary>
        /// Activate every concrete <see cref="IBindable"/> type in
        /// the given assembly that has a parameterless constructor.
        /// </summary>
        public static List<IBindable> LoadFromAssembly(Assembly assembly)
        {
            var bindings = new List<IBindable>();
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var t in types)
            {
                if (!typeof(IBindable).IsAssignableFrom(t)) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                try
                {
                    if (Activator.CreateInstance(t) is IBindable b)
                        bindings.Add(b);
                }
                catch
                {
                    // Skip types whose default ctor throws — they're
                    // not auto-activatable. Callers needing richer
                    // configuration should pass the constructed
                    // binding through the library API instead.
                }
            }

            return bindings;
        }
    }
}
