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
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Wraps a transpiled Module class instance for use in equivalence tests.
    /// Provides reflection-based access to exports and manages the Module
    /// lifecycle through the IExports/IImports interface path.
    ///
    /// This exercises the same code path that a standalone consumer would use:
    ///   var module = new WasmModule(new MyImports());
    ///   int result = module.ExportedFunc(42);
    /// </summary>
    public class TranspiledModuleWrapper
    {
        public TranspilationResult Result { get; }
        public object? ModuleInstance { get; private set; }
        public Type? ModuleClass => Result.ModuleClass;
        public Type? ExportsInterface => Result.ExportsInterface;
        public Type? ImportsInterface => Result.ImportsInterface;

        private readonly Dictionary<string, MethodInfo> _exportMethods = new();

        public TranspiledModuleWrapper(TranspilationResult result)
        {
            Result = result;
        }

        /// <summary>
        /// Instantiate the Module class with the given IImports proxy (or null for no imports).
        /// This is the standalone path: Module constructor → InitializationHelper → ThinContext.
        /// </summary>
        public void Instantiate(object? importsProxy = null)
        {
            if (ModuleClass == null)
                throw new InvalidOperationException("No Module class was generated");

            try
            {
                ModuleInstance = importsProxy != null
                    ? Activator.CreateInstance(ModuleClass, importsProxy)
                    : Activator.CreateInstance(ModuleClass);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            }

            // Cache export methods for fast lookup
            _exportMethods.Clear();
            if (ExportsInterface != null && ModuleInstance != null)
            {
                foreach (var method in ModuleClass.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!method.IsSpecialName) // skip property getters
                        _exportMethods[method.Name] = method;
                }
            }
        }

        /// <summary>
        /// Invoke an exported function by name.
        /// Returns the result as object (null for void, boxed value type for scalars).
        /// Throws TrapException for WASM traps.
        /// </summary>
        public Value[] InvokeExport(string name, Value[] args)
        {
            if (ModuleInstance == null)
                throw new InvalidOperationException("Module not instantiated");

            // Find the export method by WASM name (sanitized)
            var sanitized = InterfaceGenerator.SanitizeName(name);
            if (!_exportMethods.TryGetValue(sanitized, out var method))
                throw new InvalidOperationException($"Export '{name}' (sanitized: '{sanitized}') not found");

            // Convert Value[] args to CLR-typed args
            var parameters = method.GetParameters();
            var clrArgs = new object?[parameters.Length];
            int argIdx = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsOut) continue; // out params handled after invocation
                if (argIdx < args.Length)
                    clrArgs[i] = ConvertFromValue(args[argIdx++], parameters[i].ParameterType);
            }

            object? result;
            try
            {
                result = method.Invoke(ModuleInstance, clrArgs);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                var inner = tie.InnerException;
                // Convert CLR arithmetic/memory exceptions to WASM traps
                if (inner is DivideByZeroException)
                    throw new TrapException("integer divide by zero");
                if (inner is OverflowException)
                    throw new TrapException("integer overflow");
                if (inner is IndexOutOfRangeException)
                    throw new TrapException("out of bounds memory access");
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(inner);
                return Array.Empty<Value>(); // unreachable
            }

            // Collect results: return value + out params
            var resultType = method.ReturnType;
            var results = new List<Value>();

            if (resultType != typeof(void) && result != null)
                results.Add(ConvertToValue(result, resultType));

            // Collect out param values
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsOut && clrArgs[i] != null)
                    results.Add(ConvertToValue(clrArgs[i]!, parameters[i].ParameterType.GetElementType()!));
            }

            return results.ToArray();
        }

        /// <summary>
        /// Get export method names for building import handlers from this module's exports.
        /// </summary>
        public IReadOnlyDictionary<string, MethodInfo> ExportMethodMap => _exportMethods;

        /// <summary>
        /// Create an import handler function that delegates to an export on this module.
        /// Used to wire module A's exports as module B's imports.
        /// </summary>
        public Func<object?[], object?> CreateExportHandler(string exportName)
        {
            var sanitized = InterfaceGenerator.SanitizeName(exportName);
            if (!_exportMethods.TryGetValue(sanitized, out var method))
                throw new InvalidOperationException($"Export '{exportName}' not found");

            return args => method.Invoke(ModuleInstance, args);
        }

        private static object? ConvertFromValue(Value val, Type targetType)
        {
            if (targetType == typeof(int)) return val.Data.Int32;
            if (targetType == typeof(long)) return val.Data.Int64;
            if (targetType == typeof(float)) return val.Data.Float32;
            if (targetType == typeof(double)) return val.Data.Float64;
            return val; // reference types stay as Value
        }

        private static Value ConvertToValue(object obj, Type sourceType)
        {
            if (sourceType == typeof(int)) return new Value((int)obj);
            if (sourceType == typeof(long)) return new Value((long)obj);
            if (sourceType == typeof(float)) return new Value((float)obj);
            if (sourceType == typeof(double)) return new Value((double)obj);
            if (obj is Value v) return v;
            return default;
        }
    }
}
