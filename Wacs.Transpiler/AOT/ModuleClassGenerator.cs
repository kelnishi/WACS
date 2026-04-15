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
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using WasmModule = Wacs.Core.Module;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Generates the Module class for a transpiled WASM assembly.
    ///
    /// The module class is the primary consumer-facing type:
    ///   - Implements IExports (typed methods for each WASM export)
    ///   - Constructor accepts IImports (typed methods the consumer provides)
    ///   - Manages TranspiledContext internally
    ///   - Exposes Memory, Tables, Globals as properties
    ///   - Provides Start() if the module has a start function
    ///
    /// Usage by consumers:
    ///   var module = new WasmModule(new MyImports());
    ///   int result = module.add(2, 3);
    ///   byte[] memory = module.Memory;
    /// </summary>
    public class ModuleClassGenerator
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly string _namespace;
        private readonly WasmModule _wasmModule;
        private readonly InterfaceGenerator _interfaces;
        private readonly TypeBuilder _functionsType;
        private readonly MethodBuilder[] _methodBuilders;
        private readonly int _importCount;
        private readonly int _memoryCount;
        private readonly bool _hasStart;
        private readonly int _startFuncIndex;

        public TypeBuilder? ModuleType { get; private set; }

        public ModuleClassGenerator(
            ModuleBuilder moduleBuilder,
            string @namespace,
            WasmModule wasmModule,
            InterfaceGenerator interfaces,
            TypeBuilder functionsType,
            MethodBuilder[] methodBuilders,
            int importCount)
        {
            _moduleBuilder = moduleBuilder;
            _namespace = @namespace;
            _wasmModule = wasmModule;
            _interfaces = interfaces;
            _functionsType = functionsType;
            _methodBuilders = methodBuilders;
            _importCount = importCount;
            _memoryCount = wasmModule.Memories.Count;
            _hasStart = wasmModule.StartIndex != FuncIdx.Default;
            _startFuncIndex = _hasStart ? (int)wasmModule.StartIndex.Value : -1;
        }

        /// <summary>
        /// Generate the Module class. Must be called AFTER function IL bodies are emitted,
        /// but BEFORE CreateType() on the Functions TypeBuilder (since we reference its methods).
        /// </summary>
        public void Generate()
        {
            var parentInterfaces = _interfaces.ExportsInterface != null
                ? new[] { _interfaces.ExportsInterface }
                : Type.EmptyTypes;

            ModuleType = _moduleBuilder.DefineType(
                $"{_namespace}.Module",
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(object),
                parentInterfaces);

            // Private field: TranspiledContext
            var ctxField = ModuleType.DefineField(
                "_ctx", typeof(TranspiledContext), FieldAttributes.Private);

            // Constructor
            EmitConstructor(ctxField);

            // Export method implementations
            EmitExportMethods(ctxField);

            // Memory properties
            EmitMemoryProperties(ctxField);

            // Start method
            if (_hasStart)
                EmitStartMethod(ctxField);

            // ModuleName property
            EmitModuleNameProperty();
        }

        /// <summary>
        /// Bake the type. Call after Generate() and after Functions type is created.
        /// </summary>
        public Type? CreateType() => ModuleType?.CreateType();

        private void EmitConstructor(FieldBuilder ctxField)
        {
            // Constructor parameters: IImports (if any), optional memory pages
            var ctorParams = _interfaces.ImportsInterface != null
                ? new[] { _interfaces.ImportsInterface }
                : Type.EmptyTypes;

            var ctor = ModuleType!.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParams);

            if (_interfaces.ImportsInterface != null)
                ctor.DefineParameter(1, ParameterAttributes.None, "imports");

            var il = ctor.GetILGenerator();

            // Call base()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

            // Create TranspiledContext
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, typeof(TranspiledContext).GetConstructor(
                new[] { typeof(byte[][]), typeof(TableInstance[]), typeof(GlobalInstance[]),
                        typeof(Delegate[]), typeof(Delegate[]) })!);
            il.Emit(OpCodes.Stfld, ctxField);

            // TODO: Initialize memories from module data
            // TODO: Wire import delegates from IImports interface methods
            // TODO: Populate FuncTable with transpiled method delegates

            il.Emit(OpCodes.Ret);
        }

        private void EmitExportMethods(FieldBuilder ctxField)
        {
            foreach (var export in _interfaces.ExportMethods)
            {
                // The export interface method was defined by InterfaceGenerator.
                // We need to implement it by forwarding to Functions.FunctionN

                int localIdx = export.FuncIndex - _importCount;
                if (localIdx < 0 || localIdx >= _methodBuilders.Length) continue;

                var targetMethod = _methodBuilders[localIdx];
                var funcType = export.WasmType;
                int paramCount = funcType.ParameterTypes.Arity;
                var resultTypes = funcType.ResultType.Types;
                int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

                // Build the method signature to match the interface
                var clrParamTypes = funcType.ParameterTypes.Types
                    .Select(t => ModuleTranspiler.MapValType(t)).ToArray();
                var allParamTypes = new Type[clrParamTypes.Length + outParamCount];
                Array.Copy(clrParamTypes, allParamTypes, clrParamTypes.Length);
                for (int i = 0; i < outParamCount; i++)
                    allParamTypes[clrParamTypes.Length + i] =
                        ModuleTranspiler.MapValType(resultTypes[i + 1]).MakeByRefType();

                Type returnType = resultTypes.Length >= 1
                    ? ModuleTranspiler.MapValType(resultTypes[0])
                    : typeof(void);

                var mb = ModuleType!.DefineMethod(
                    export.Name,
                    MethodAttributes.Public | MethodAttributes.Virtual,
                    returnType,
                    allParamTypes);

                // Name parameters
                for (int p = 0; p < clrParamTypes.Length; p++)
                    mb.DefineParameter(p + 1, ParameterAttributes.None, $"param{p}");
                for (int r = 0; r < outParamCount; r++)
                    mb.DefineParameter(clrParamTypes.Length + r + 1, ParameterAttributes.Out, $"result{r + 1}");

                // Emit body: forward to Functions.FunctionN(ctx, params...)
                var il = mb.GetILGenerator();

                // Push ctx
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ctxField);

                // Push all params (arg 1..N on the instance method = params for the static method)
                for (int p = 0; p < clrParamTypes.Length; p++)
                    il.Emit(OpCodes.Ldarg, p + 1);

                // Push out param addresses
                for (int r = 0; r < outParamCount; r++)
                    il.Emit(OpCodes.Ldarg, clrParamTypes.Length + r + 1);

                il.Emit(OpCodes.Call, targetMethod);
                il.Emit(OpCodes.Ret);
            }
        }

        private void EmitMemoryProperties(FieldBuilder ctxField)
        {
            if (_memoryCount == 0) return;

            // Memory property: byte[] Memory => _ctx.Memories[0]
            var memProp = ModuleType!.DefineProperty(
                "Memory", PropertyAttributes.None, typeof(byte[]), null);

            var getter = ModuleType.DefineMethod(
                "get_Memory",
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(byte[]), Type.EmptyTypes);

            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Ldfld, typeof(TranspiledContext).GetField(nameof(TranspiledContext.Memories))!);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ret);

            memProp.SetGetMethod(getter);

            // MemoryCount property
            var countProp = ModuleType.DefineProperty(
                "MemoryCount", PropertyAttributes.None, typeof(int), null);
            var countGetter = ModuleType.DefineMethod(
                "get_MemoryCount",
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(int), Type.EmptyTypes);
            var il2 = countGetter.GetILGenerator();
            il2.Emit(OpCodes.Ldc_I4, _memoryCount);
            il2.Emit(OpCodes.Ret);
            countProp.SetGetMethod(countGetter);
        }

        private void EmitStartMethod(FieldBuilder ctxField)
        {
            int localIdx = _startFuncIndex - _importCount;
            if (localIdx < 0 || localIdx >= _methodBuilders.Length) return;

            var targetMethod = _methodBuilders[localIdx];

            var mb = ModuleType!.DefineMethod(
                "Start",
                MethodAttributes.Public,
                typeof(void), Type.EmptyTypes);

            var il = mb.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Call, targetMethod);
            il.Emit(OpCodes.Ret);
        }

        private void EmitModuleNameProperty()
        {
            string name = _namespace.Split('.').LastOrDefault() ?? "WasmModule";

            var prop = ModuleType!.DefineProperty(
                "ModuleName", PropertyAttributes.None, typeof(string), null);
            var getter = ModuleType.DefineMethod(
                "get_ModuleName",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Static,
                typeof(string), Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ret);
            prop.SetGetMethod(getter);
        }
    }
}
