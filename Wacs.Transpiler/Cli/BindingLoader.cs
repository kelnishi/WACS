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

namespace Wacs.Transpiler.Cli
{
    /// <summary>
    /// Loads host-binding assemblies (libraries that expose types
    /// implementing <see cref="IBindable"/>) and activates each discovered
    /// binding with a parameterless constructor. Used by <c>--bind</c> so
    /// <c>wasm-transpile</c> can accept any WACS-compatible host library —
    /// WASI, a game engine host, a custom syscall shim — without hard-coding
    /// each one into the CLI.
    /// </summary>
    public static class BindingLoader
    {
        /// <summary>
        /// Load the assembly at <paramref name="path"/>, find every concrete
        /// <see cref="IBindable"/> that has a parameterless constructor,
        /// instantiate it, and return the resulting bindings. Caller is
        /// responsible for calling <see cref="IBindable.BindToRuntime"/> and
        /// (for IDisposable bindings) disposing at shutdown.
        /// </summary>
        public static List<IBindable> LoadFromAssembly(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"binding assembly not found: {fullPath}");

            var asm = Assembly.LoadFrom(fullPath);
            return LoadFromAssembly(asm);
        }

        /// <summary>
        /// Activate every concrete <see cref="IBindable"/> type in the given
        /// assembly that has a parameterless constructor. Used by the
        /// library API when callers have the assembly in hand already.
        /// </summary>
        public static List<IBindable> LoadFromAssembly(Assembly assembly)
        {
            var bindings = new List<IBindable>();
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                // Partial load — use whatever loaded successfully.
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var t in types)
            {
                if (!typeof(IBindable).IsAssignableFrom(t)) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                // Require a parameterless ctor; otherwise we can't auto-activate.
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                try
                {
                    if (Activator.CreateInstance(t) is IBindable b)
                        bindings.Add(b);
                }
                catch
                {
                    // Skip types whose default ctor throws — they're not
                    // auto-activatable. Callers who need richer configuration
                    // should pass the constructed binding through the library
                    // API instead of relying on --bind discovery.
                }
            }

            return bindings;
        }
    }
}
