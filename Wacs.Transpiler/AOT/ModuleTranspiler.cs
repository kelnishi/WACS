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
using Wacs.Transpiler.AOT.Emitters;

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

        /// <summary>Function types for all functions in module index space (imports + locals).</summary>
        public FunctionType[] AllFunctionTypes { get; }

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
            IReadOnlyList<InterfaceMethod> importMethods,
            FunctionType[]? allFunctionTypes = null)
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
            AllFunctionTypes = allFunctionTypes ?? Array.Empty<FunctionType>();
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

            // Register element segments for standalone table.init
            int elemSegmentBaseId = -1;
            for (int e = 0; e < moduleInst.Repr.Elements.Length; e++)
            {
                var elem = moduleInst.Repr.Elements[e];
                var values = new Value[elem.Initializers.Length];
                for (int i = 0; i < elem.Initializers.Length; i++)
                {
                    var expr = elem.Initializers[i];
                    if (expr.Instructions.Count > 0 &&
                        expr.Instructions[0] is Wacs.Core.Instructions.Reference.InstRefFunc rf)
                        values[i] = new Value(ValType.FuncRef, (int)rf.FunctionIndex.Value);
                    else
                        values[i] = new Value(ValType.Nil); // ref.null or other
                }
                int id = ModuleInit.RegisterElemSegment(values);
                if (e == 0) elemSegmentBaseId = id;
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
                    EmitFallbackBody(mb, importCount + i, funcInst.Type);
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
                dataEmitter, dataSegmentBaseId >= 0 ? dataSegmentBaseId : 0,
                elemSegmentBaseId >= 0 ? elemSegmentBaseId : 0,
                allFunctionTypes);
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
                interfaceGen.ImportMethods,
                allFunctionTypes);
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

        /// <summary>
        /// Emit a fallback body that dispatches through the interpreter
        /// instead of throwing NotSupportedException. This allows transpiled
        /// functions to call non-transpiled siblings seamlessly.
        ///
        /// The fallback packs CLR-typed parameters into Value[], calls
        /// CallHelpers.InvokeFallback, and unpacks the result.
        /// </summary>
        private void EmitFallbackBody(MethodBuilder mb, int moduleFuncIndex, FunctionType funcType)
        {
            var il = mb.GetILGenerator();
            var paramTypes = funcType.ParameterTypes.Types;
            var resultTypes = funcType.ResultType.Types;

            // Build Value[] args from the CLR-typed parameters
            // arg 0 = TranspiledContext ctx, args 1..N = WASM params
            il.Emit(OpCodes.Ldc_I4, paramTypes.Length);
            il.Emit(OpCodes.Newarr, typeof(Value));

            for (int p = 0; p < paramTypes.Length; p++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, p);
                il.Emit(OpCodes.Ldarg, p + 1); // skip ctx at arg 0
                EmitBoxToValue(il, paramTypes[p]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }

            var argsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Call InvokeFallback(ctx, funcIndex, args) → Value[]
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, moduleFuncIndex);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.InvokeFallback),
                BindingFlags.Public | BindingFlags.Static)!);

            // Unpack result
            if (resultTypes.Length == 0)
            {
                il.Emit(OpCodes.Pop); // discard Value[]
            }
            else
            {
                // Result 0 is CLR return value
                var resultsLocal = il.DeclareLocal(typeof(Value[]));
                il.Emit(OpCodes.Stloc, resultsLocal);

                il.Emit(OpCodes.Ldloc, resultsLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem, typeof(Value));
                EmitUnboxFromValue(il, resultTypes[0]);

                // Results 1..N go to out params
                for (int r = 1; r < resultTypes.Length; r++)
                {
                    il.Emit(OpCodes.Ldarg, 1 + paramTypes.Length + (r - 1)); // out param
                    il.Emit(OpCodes.Ldloc, resultsLocal);
                    il.Emit(OpCodes.Ldc_I4, r);
                    il.Emit(OpCodes.Ldelem, typeof(Value));
                    EmitUnboxFromValue(il, resultTypes[r]);
                    EmitStind(il, resultTypes[r]);
                }
            }

            il.Emit(OpCodes.Ret);
        }

        private static void EmitBoxToValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    break;
                default:
                    break; // Reference types are already Value on the CIL stack
            }
        }

        private static readonly FieldInfo DataField =
            typeof(Value).GetField(nameof(Value.Data))!;
        private static readonly FieldInfo Int32Field =
            typeof(DUnion).GetField(nameof(DUnion.Int32))!;
        private static readonly FieldInfo Int64Field =
            typeof(DUnion).GetField(nameof(DUnion.Int64))!;
        private static readonly FieldInfo Float32Field =
            typeof(DUnion).GetField(nameof(DUnion.Float32))!;
        private static readonly FieldInfo Float64Field =
            typeof(DUnion).GetField(nameof(DUnion.Float64))!;

        private static void EmitUnboxFromValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                case ValType.I64:
                case ValType.F32:
                case ValType.F64:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, type switch
                    {
                        ValType.I32 => Int32Field,
                        ValType.I64 => Int64Field,
                        ValType.F32 => Float32Field,
                        ValType.F64 => Float64Field,
                        _ => throw new InvalidOperationException()
                    });
                    break;
                }
                default:
                    break; // Reference types stay as Value
            }
        }

        private static void EmitStind(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32: il.Emit(OpCodes.Stind_I4); break;
                case ValType.I64: il.Emit(OpCodes.Stind_I8); break;
                case ValType.F32: il.Emit(OpCodes.Stind_R4); break;
                case ValType.F64: il.Emit(OpCodes.Stind_R8); break;
                default: il.Emit(OpCodes.Stobj, typeof(Value)); break;
            }
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
