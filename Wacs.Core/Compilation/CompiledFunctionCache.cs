// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Concurrent;
using System.Linq;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Process-wide cache of <see cref="CompiledFunction"/> keyed by <see cref="FunctionInstance"/>.
    /// Lazy: the first Call through the switch runtime compiles; subsequent calls reuse.
    ///
    /// Reference-keyed — one entry per live FunctionInstance. The cache holds strong refs,
    /// so entries live until process exit. That's fine for the current module lifetime story
    /// (no unload); revisit if/when module unload lands.
    ///
    /// AOT-safe: plain ConcurrentDictionary, no reflection.
    /// </summary>
    internal static class CompiledFunctionCache
    {
        private static readonly ConcurrentDictionary<FunctionInstance, CompiledFunction> _cache = new();

        public static CompiledFunction GetOrCompile(FunctionInstance inst)
        {
            if (_cache.TryGetValue(inst, out var cached)) return cached;

            // Body.Instructions have already been Link()'d during module instantiation, so
            // any BlockTarget / LinkedLabel state BytecodeCompiler might need is populated.
            var linked = inst.Body.Instructions.Flatten().ToArray();
            var compiled = BytecodeCompiler.Compile(
                linked,
                inst.Type,
                // Total locals = params + declared locals.
                localsCount: inst.Type.ParameterTypes.Arity + inst.Locals.Length);

            // First-wins under concurrency — ConcurrentDictionary.GetOrAdd would call the
            // factory twice on races; we compile-once + try-add to avoid wasted work on the
            // second racer.
            return _cache.TryAdd(inst, compiled) ? compiled : _cache[inst];
        }
    }
}
