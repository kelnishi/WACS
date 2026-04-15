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

        /// <summary>Diagnostics emitted during transpilation.</summary>
        public IReadOnlyList<TranspilerDiagnostic> Diagnostics { get; }

        /// <summary>Generated interface for module exports. Null if no exports.</summary>
        public Type? ExportsInterface { get; }

        /// <summary>Generated interface for module imports. Null if no imports.</summary>
        public Type? ImportsInterface { get; }

        /// <summary>Generated Module class (implements IExports, accepts IImports).</summary>
        public Type? ModuleClass { get; }

        /// <summary>Export method metadata (name, type, index).</summary>
        public IReadOnlyList<InterfaceMethod> ExportMethods { get; }

        /// <summary>Import method metadata (name, type, index).</summary>
        public IReadOnlyList<InterfaceMethod> ImportMethods { get; }

        public TranspilationResult(
            Assembly assembly,
            Type functionsType,
            MethodInfo[] methods,
            ModuleMetadata.Manifest manifest,
            Dictionary<int, MethodInfo> functionMethodMap,
            IReadOnlyList<TranspilerDiagnostic> diagnostics,
            Type? exportsInterface,
            Type? importsInterface,
            Type? moduleClass,
            IReadOnlyList<InterfaceMethod> exportMethods,
            IReadOnlyList<InterfaceMethod> importMethods)
        {
            Assembly = assembly;
            FunctionsType = functionsType;
            Methods = methods;
            Manifest = manifest;
            FunctionMethodMap = functionMethodMap;
            Diagnostics = diagnostics;
            ExportsInterface = exportsInterface;
            ImportsInterface = importsInterface;
            ModuleClass = moduleClass;
            ExportMethods = exportMethods;
            ImportMethods = importMethods;
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
        private readonly TranspilerOptions _options;

        public ModuleTranspiler(string @namespace = "Wacs.Transpiled", TranspilerOptions? options = null)
        {
            _namespace = @namespace;
            _options = options ?? new TranspilerOptions();
        }

        /// <summary>
        /// Transpile all functions in a module instance to a dynamic .NET assembly.
        /// </summary>
        public TranspilationResult Transpile(
            ModuleInstance moduleInst,
            WasmRuntime runtime,
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

            // Collect all wasm functions and count imports
            var wasmFunctions = new List<FunctionInstance>();
            int importCount = 0;
            bool foundLocal = false;
            foreach (var funcAddr in moduleInst.FuncAddrs)
            {
                var func = runtime.GetFunction(funcAddr);
                if (func is FunctionInstance fi && fi.Module == moduleInst)
                {
                    foundLocal = true;
                    wasmFunctions.Add(fi);
                }
                else if (!foundLocal)
                {
                    importCount++;
                }
            }

            // Pre-resolve all function types (imports + locals) for call site resolution
            var allFunctionTypes = new FunctionType[importCount + wasmFunctions.Count];
            int fIdx = 0;
            foreach (var funcAddr in moduleInst.FuncAddrs)
            {
                var func = runtime.GetFunction(funcAddr);
                allFunctionTypes[fIdx++] = func.Type;
                if (fIdx >= allFunctionTypes.Length) break;
            }

            var diagnostics = new DiagnosticCollector();

            // === Analyze data segments ===
            var dataEmitter = new DataSegmentEmitter(moduleInst.Repr, _options.DataStorage, diagnostics);
            dataEmitter.Analyze();
            // Register ALL segment data for runtime access (dynamic assemblies).
            // Track the base segment ID so PrepareInitData can compute absolute IDs.
            int dataSegmentBaseId = -1;
            for (int s = 0; s < dataEmitter.Segments.Length; s++)
            {
                int id = ModuleInit.RegisterDataSegment(dataEmitter.Segments[s].Data);
                if (s == 0) dataSegmentBaseId = id;
            }

            // === Pass 0a: Generate typed interfaces for exports and imports ===
            var interfaceGen = new InterfaceGenerator(
                moduleBuilder, $"{_namespace}.{moduleName}",
                moduleInst.Repr, moduleInst, runtime, importCount);
            interfaceGen.Generate();

            // === Pass 0b: Emit CLR types for WASM struct/array definitions ===
            var gcTypeEmitter = new GcTypeEmitter(moduleBuilder, $"{_namespace}.{moduleName}", moduleInst.Types);
            gcTypeEmitter.EmitTypes();

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
                var codegen = new FunctionCodegen(mb, funcInst, wasmFunctions.ToArray(), methodBuilders, importCount, gcTypeEmitter, allFunctionTypes, _options, diagnostics);

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
                    ExportName = funcInst.IsExport ? funcInst.Name : null,
                    RejectionReason = emitted ? null : codegen.LastRejectionReason
                });

                if (emitted)
                    manifest.TranspiledCount++;
                else
                    manifest.FallbackCount++;
            }

            // === Generate Module class (implements IExports, accepts IImports) ===
            var moduleClassGen = new ModuleClassGenerator(
                moduleBuilder, $"{_namespace}.{moduleName}",
                moduleInst.Repr, interfaceGen, typeBuilder, methodBuilders, importCount,
                dataEmitter, dataSegmentBaseId >= 0 ? dataSegmentBaseId : 0);
            moduleClassGen.Generate();

            // Finalize the types
            var functionsType = typeBuilder.CreateType()!;
            var moduleClassType = moduleClassGen.CreateType();

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
                methodMap,
                diagnostics.Diagnostics,
                interfaceGen.ExportsInterface?.UnderlyingSystemType,
                interfaceGen.ImportsInterface?.UnderlyingSystemType,
                moduleClassType,
                interfaceGen.ExportMethods,
                interfaceGen.ImportMethods);
        }

        private MethodBuilder CreateMethodStub(
            TypeBuilder typeBuilder,
            FunctionInstance funcInst,
            FunctionType funcType,
            int index)
        {
            // Build parameter types: TranspiledContext + wasm params + out params for multi-value
            var wasmParamTypes = funcType.ParameterTypes.Types;
            var resultTypes = funcType.ResultType.Types;
            int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

            var paramTypes = new Type[1 + wasmParamTypes.Length + outParamCount];
            paramTypes[0] = typeof(TranspiledContext);
            for (int p = 0; p < wasmParamTypes.Length; p++)
            {
                paramTypes[p + 1] = MapValType(wasmParamTypes[p]);
            }
            // Out params for results 1..N (result 0 is the CLR return value)
            for (int r = 0; r < outParamCount; r++)
            {
                paramTypes[1 + wasmParamTypes.Length + r] = MapValType(resultTypes[r + 1]).MakeByRefType();
            }

            // Return type: result 0, or void if no results
            Type returnType = resultTypes.Length >= 1
                ? MapValType(resultTypes[0])
                : typeof(void);

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
            for (int r = 0; r < outParamCount; r++)
            {
                mb.DefineParameter(2 + wasmParamTypes.Length + r, ParameterAttributes.Out, $"result{r + 1}");
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
