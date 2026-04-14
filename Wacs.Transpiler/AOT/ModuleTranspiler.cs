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
using System.Reflection.Emit;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Result of transpiling a module, containing the generated assembly
    /// and metadata needed to wire it into the WACS runtime.
    /// </summary>
    public class TranspilationResult
    {
        public Assembly Assembly { get; }
        public Type FunctionsType { get; }
        public MethodInfo[] Methods { get; }
        public ModuleMetadata.Manifest Manifest { get; }

        /// <summary>
        /// Maps wasm function index (within the module's locally-defined functions)
        /// to the corresponding MethodInfo in the transpiled assembly.
        /// </summary>
        public IReadOnlyDictionary<int, MethodInfo> FunctionMethodMap { get; }

        public int TranspiledCount => Manifest.TranspiledCount;
        public int FallbackCount => Manifest.FallbackCount;

        public TranspilationResult(
            Assembly assembly,
            Type functionsType,
            MethodInfo[] methods,
            ModuleMetadata.Manifest manifest,
            Dictionary<int, MethodInfo> functionMethodMap)
        {
            Assembly = assembly;
            FunctionsType = functionsType;
            Methods = methods;
            Manifest = manifest;
            FunctionMethodMap = functionMethodMap;
        }
    }

    /// <summary>
    /// Orchestrates the transpilation of a WebAssembly module into a .NET assembly.
    ///
    /// Two-pass approach:
    ///   Pass 1: Create MethodBuilder stubs for every function with correct signatures.
    ///   Pass 2: Emit IL bodies (or fallback stubs) for each function.
    /// </summary>
    public class ModuleTranspiler
    {
        private readonly string _namespace;

        public ModuleTranspiler(string @namespace = "Wacs.Transpiled")
        {
            _namespace = @namespace;
        }

        /// <summary>
        /// Transpile all functions in a module instance to a dynamic .NET assembly.
        /// </summary>
        public TranspilationResult Transpile(
            ModuleInstance moduleInst,
            Store store,
            string moduleName = "WasmModule")
        {
            var assemblyName = new AssemblyName($"{_namespace}.{moduleName}");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

            var typeBuilder = moduleBuilder.DefineType(
                $"{_namespace}.{moduleName}.Functions",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

            // Collect all wasm functions (skip host functions)
            var wasmFunctions = new List<FunctionInstance>();
            foreach (var funcAddr in moduleInst.FuncAddrs)
            {
                var func = store[funcAddr];
                if (func is FunctionInstance fi && fi.Module == moduleInst)
                {
                    wasmFunctions.Add(fi);
                }
            }

            // === Pass 1: Create method stubs ===
            var methodBuilders = new MethodBuilder[wasmFunctions.Count];
            for (int i = 0; i < wasmFunctions.Count; i++)
            {
                var funcInst = wasmFunctions[i];
                var funcType = funcInst.Type;
                methodBuilders[i] = CreateMethodStub(typeBuilder, funcInst, funcType, i);
            }

            // === Pass 2: Emit IL bodies ===
            var manifest = new ModuleMetadata.Manifest
            {
                ModuleName = moduleName,
                Namespace = _namespace,
                FunctionsTypeName = $"{_namespace}.{moduleName}.Functions"
            };

            for (int i = 0; i < wasmFunctions.Count; i++)
            {
                var funcInst = wasmFunctions[i];
                var mb = methodBuilders[i];
                var codegen = new FunctionCodegen(mb, funcInst, methodBuilders);

                bool emitted = codegen.TryEmit();

                if (!emitted)
                {
                    EmitFallbackBody(mb, i);
                }

                manifest.Functions.Add(new ModuleMetadata.FunctionEntry
                {
                    Index = i,
                    MethodName = mb.Name,
                    IsTranspiled = emitted,
                    ExportName = funcInst.IsExport ? funcInst.Name : null
                });

                if (emitted)
                    manifest.TranspiledCount++;
                else
                    manifest.FallbackCount++;
            }

            // Finalize the type
            var functionsType = typeBuilder.CreateType()!;

            // Retrieve the actual MethodInfo objects from the baked type
            var methods = new MethodInfo[wasmFunctions.Count];
            var methodMap = new Dictionary<int, MethodInfo>();
            for (int i = 0; i < wasmFunctions.Count; i++)
            {
                methods[i] = functionsType.GetMethod(methodBuilders[i].Name)!;
                methodMap[i] = methods[i];
            }

            return new TranspilationResult(
                assemblyBuilder,
                functionsType,
                methods,
                manifest,
                methodMap);
        }

        private MethodBuilder CreateMethodStub(
            TypeBuilder typeBuilder,
            FunctionInstance funcInst,
            FunctionType funcType,
            int index)
        {
            // Build parameter types: TranspiledContext + wasm params
            var wasmParamTypes = funcType.ParameterTypes.Types;
            var paramTypes = new Type[wasmParamTypes.Length + 1];
            paramTypes[0] = typeof(TranspiledContext);
            for (int p = 0; p < wasmParamTypes.Length; p++)
            {
                paramTypes[p + 1] = MapValType(wasmParamTypes[p]);
            }

            // Return type
            Type returnType;
            switch (funcType.ResultType.Arity)
            {
                case 0:
                    returnType = typeof(void);
                    break;
                case 1:
                    returnType = MapValType(funcType.ResultType.Types[0]);
                    break;
                default:
                    // Multi-value: fall back to Value[] for now
                    // TODO: Use WasmReturn<T1,T2> structs in Phase 3
                    returnType = typeof(Value[]);
                    break;
            }

            string methodName = !string.IsNullOrEmpty(funcInst.Name)
                ? $"Function_{funcInst.Name}"
                : $"Function{index}";

            var mb = typeBuilder.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes);

            // Name parameters for debuggability
            mb.DefineParameter(1, ParameterAttributes.None, "ctx");
            for (int p = 0; p < wasmParamTypes.Length; p++)
            {
                mb.DefineParameter(p + 2, ParameterAttributes.None, $"param{p}");
            }

            return mb;
        }

        private void EmitFallbackBody(MethodBuilder mb, int funcIndex)
        {
            var il = mb.GetILGenerator();
            il.Emit(OpCodes.Ldstr, $"Function {funcIndex} not yet transpiled — use interpreter fallback");
            il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
        }

        /// <summary>
        /// Maps a WebAssembly value type to the corresponding CLR type for use
        /// in transpiled method signatures.
        /// </summary>
        internal static Type MapValType(ValType type)
        {
            return type switch
            {
                ValType.I32 => typeof(int),
                ValType.I64 => typeof(long),
                ValType.F32 => typeof(float),
                ValType.F64 => typeof(double),
                // Reference types and V128 stay as Value on the CLR stack
                _ => typeof(Value)
            };
        }
    }
}
