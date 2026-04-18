// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Wacs.Transpiler.Cli
{
    /// <summary>
    /// DispatchProxy that implements a generated IImports interface by
    /// routing each import method call to a handler registered by name.
    /// Used by <see cref="WasiRunner"/> to bridge transpiled-module imports
    /// to the interpreter's bound WASI host functions.
    /// </summary>
    public class ImportDispatcher : DispatchProxy
    {
        private readonly Dictionary<string, Func<object?[], object?>> _handlers = new();

        public void Register(string methodName, Func<object?[], object?> handler)
        {
            _handlers[methodName] = handler;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) return null;
            if (_handlers.TryGetValue(targetMethod.Name, out var handler))
                return handler(args ?? Array.Empty<object?>());

            if (targetMethod.ReturnType == typeof(void)) return null;
            if (targetMethod.ReturnType.IsValueType)
                return Activator.CreateInstance(targetMethod.ReturnType);
            return null;
        }

        /// <summary>
        /// Create a proxy implementing the given IImports interface type,
        /// backed by an ImportDispatcher with the given handlers.
        /// </summary>
        public static object Create(Type importsInterface, Dictionary<string, Func<object?[], object?>> handlers)
        {
            var createMethod = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(importsInterface, typeof(ImportDispatcher));

            var proxy = createMethod.Invoke(null, null)!;
            var dispatcher = (ImportDispatcher)proxy;
            foreach (var kv in handlers)
                dispatcher.Register(kv.Key, kv.Value);
            return proxy;
        }
    }
}
