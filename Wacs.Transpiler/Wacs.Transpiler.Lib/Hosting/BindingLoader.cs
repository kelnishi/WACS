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
                return Assembly.LoadFrom(Path.GetFullPath(nameOrPath));
            try { return Assembly.Load(nameOrPath); }
            catch (Exception ex)
            {
                throw new FileNotFoundException(
                    "binding assembly not found as file or name: "
                    + nameOrPath + " (" + ex.Message + ")");
            }
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
