// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Creates a DispatchProxy implementing a dynamic IImports interface.
    /// Routes import method calls to registered export methods from
    /// previously transpiled modules or host functions.
    ///
    /// Import method names follow the convention: {moduleName}_{fieldName}
    /// Export method names are the WASM export names (sanitized).
    /// </summary>
    public class ImportDispatcher : DispatchProxy
    {
        private readonly Dictionary<string, Func<object?[], object?>> _handlers = new();

        /// <summary>
        /// Register a handler for an import method name.
        /// </summary>
        public void Register(string methodName, Func<object?[], object?> handler)
        {
            _handlers[methodName] = handler;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) return null;

            if (_handlers.TryGetValue(targetMethod.Name, out var handler))
            {
                return handler(args ?? Array.Empty<object?>());
            }

            // No handler registered — return default for the return type
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
            // DispatchProxy.Create<TInterface, TProxy>() — we need the generic version
            var createMethod = typeof(DispatchProxy)
                .GetMethod(nameof(DispatchProxy.Create), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(importsInterface, typeof(ImportDispatcher));

            var proxy = createMethod.Invoke(null, null)!;
            var dispatcher = (ImportDispatcher)proxy;

            foreach (var (name, handler) in handlers)
            {
                dispatcher.Register(name, handler);
            }

            return proxy;
        }
    }
}
