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
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using WasmModule = Wacs.Core.Module;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Describes a generated function on an interface.
    /// </summary>
    public class InterfaceMethod
    {
        public string Name { get; }
        public string WasmName { get; }
        public FunctionType WasmType { get; }
        public int FuncIndex { get; }
        public MethodBuilder? Method { get; set; }

        public InterfaceMethod(string name, FunctionType wasmType, int funcIndex, string? wasmName = null)
        {
            Name = name;
            WasmName = wasmName ?? name;
            WasmType = wasmType;
            FuncIndex = funcIndex;
        }
    }

    /// <summary>
    /// Generates typed interfaces for a transpiled WASM module:
    ///
    /// - IExports: one method per exported function, named from WASM export names.
    ///   Consumers call these directly with typed parameters.
    ///
    /// - IImports: one method per imported function, named from WASM import module+field.
    ///   Implementors provide these to satisfy the module's dependencies.
    ///
    /// Names are sanitized for CLR identifier rules (no dots, no keywords, etc.).
    /// The WASM name is preserved in a custom attribute for tooling.
    /// </summary>
    public class InterfaceGenerator
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly string _namespace;
        private readonly WasmModule _wasmModule;
        private readonly ModuleInstance _moduleInst;
        private readonly WasmRuntime _runtime;
        private readonly int _importCount;

        public TypeBuilder? ExportsInterface { get; private set; }
        public TypeBuilder? ImportsInterface { get; private set; }
        public List<InterfaceMethod> ExportMethods { get; } = new();
        public List<InterfaceMethod> ImportMethods { get; } = new();

        public InterfaceGenerator(
            ModuleBuilder moduleBuilder,
            string @namespace,
            WasmModule wasmModule,
            ModuleInstance moduleInst,
            WasmRuntime runtime,
            int importCount)
        {
            _moduleBuilder = moduleBuilder;
            _namespace = @namespace;
            _wasmModule = wasmModule;
            _moduleInst = moduleInst;
            _runtime = runtime;
            _importCount = importCount;
        }

        public void Generate()
        {
            GenerateImportsInterface();
            GenerateExportsInterface();
        }

        private void GenerateImportsInterface()
        {
            if (_importCount == 0) return;

            ImportsInterface = _moduleBuilder.DefineType(
                $"{_namespace}.IImports",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);

            int funcImportIdx = 0;
            foreach (var import in _wasmModule.Imports)
            {
                if (import.Desc is not WasmModule.ImportDesc.FuncDesc fd) continue;

                var funcType = _moduleInst.Types[fd.TypeIndex].Expansion as FunctionType;
                if (funcType == null) { funcImportIdx++; continue; }

                string methodName = SanitizeName($"{import.ModuleName}_{import.Name}");

                var method = DefineInterfaceMethod(ImportsInterface, methodName, funcType);
                ImportMethods.Add(new InterfaceMethod(methodName, funcType, funcImportIdx)
                {
                    Method = method
                });
                funcImportIdx++;
            }

            ImportsInterface.CreateType();
        }

        private void GenerateExportsInterface()
        {
            var exports = _wasmModule.Exports
                .Where(e => e.Desc is WasmModule.ExportDesc.FuncDesc)
                .ToList();

            if (exports.Count == 0) return;

            ExportsInterface = _moduleBuilder.DefineType(
                $"{_namespace}.IExports",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);

            var usedExportNames = new HashSet<string>();
            foreach (var export in exports)
            {
                var funcDesc = (WasmModule.ExportDesc.FuncDesc)export.Desc;
                int funcIdx = (int)funcDesc.FunctionIndex.Value;

                var func = _runtime.GetFunction(
                    _moduleInst.FuncAddrs.ElementAt(funcIdx));
                var funcType = func.Type;

                string methodName = SanitizeName(export.Name);
                // Dedup: append counter if collision
                string baseName = methodName;
                int suffix = 2;
                while (!usedExportNames.Add(methodName))
                    methodName = $"{baseName}_{suffix++}";

                var method = DefineInterfaceMethod(ExportsInterface, methodName, funcType);
                ExportMethods.Add(new InterfaceMethod(methodName, funcType, funcIdx, export.Name)
                {
                    Method = method
                });
            }

            ExportsInterface.CreateType();
        }

        private MethodBuilder DefineInterfaceMethod(TypeBuilder iface, string name, FunctionType funcType)
        {
            var paramTypes = funcType.ParameterTypes.Types
                .Select(t => ModuleTranspiler.MapValType(t)).ToArray();

            var resultTypes = funcType.ResultType.Types;
            int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

            // Build full parameter list (wasm params + out params for multi-value)
            var allParamTypes = new Type[paramTypes.Length + outParamCount];
            Array.Copy(paramTypes, allParamTypes, paramTypes.Length);
            for (int i = 0; i < outParamCount; i++)
                allParamTypes[paramTypes.Length + i] = ModuleTranspiler.MapValType(resultTypes[i + 1]).MakeByRefType();

            Type returnType = resultTypes.Length >= 1
                ? ModuleTranspiler.MapValType(resultTypes[0])
                : typeof(void);

            var mb = iface.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract,
                returnType,
                allParamTypes);

            // Name parameters
            for (int i = 0; i < paramTypes.Length; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None, $"param{i}");
            for (int i = 0; i < outParamCount; i++)
                mb.DefineParameter(paramTypes.Length + i + 1, ParameterAttributes.Out, $"result{i + 1}");

            return mb;
        }

        /// <summary>
        /// Sanitize a WASM name for use as a CLR identifier.
        /// </summary>
        /// <summary>
        /// Sanitize a WASM name for use as a CLR method name.
        /// Replaces invalid chars with _, prefixes digits, avoids reserved words.
        /// NOTE: this can produce collisions (e.g., "0" and "-0" both → "_0").
        /// Use SanitizeNameUnique for deduplication.
        /// </summary>
        public static string SanitizeName(string wasmName)
        {
            if (string.IsNullOrEmpty(wasmName))
                return "_unnamed";

            var chars = wasmName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            }

            var result = new string(chars);

            // Can't start with a digit
            if (char.IsDigit(result[0]))
                result = "_" + result;

            // Avoid CLR reserved words
            if (result is "abstract" or "class" or "interface" or "new" or "return"
                or "void" or "int" or "long" or "float" or "double" or "string"
                or "bool" or "object" or "null" or "true" or "false")
                result = "_" + result;

            return result;
        }
    }
}
