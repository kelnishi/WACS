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
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Describes an emitted CLR type corresponding to a WASM struct or array definition.
    /// </summary>
    public class EmittedGcType
    {
        public int TypeIndex { get; }
        public Type ClrType { get; set; } = null!;
        public TypeBuilder? TypeBuilder { get; set; }

        /// <summary>
        /// Structural hash for cross-module type equivalence.
        /// Computed from field types/mutability, consistent with Wacs.Core.Utilities.StableHash.
        /// </summary>
        public int StructuralHash { get; set; }

        /// <summary>
        /// For structs: field builders indexed by FieldIdx.
        /// For arrays: element field builder at index 0, length field at index 1.
        /// </summary>
        public FieldBuilder[] Fields { get; set; } = Array.Empty<FieldBuilder>();

        /// <summary>
        /// Whether this is a struct (true) or array (false).
        /// </summary>
        public bool IsStruct { get; set; }

        public EmittedGcType(int typeIndex) => TypeIndex = typeIndex;
    }

    /// <summary>
    /// Emits CLR classes for WASM struct and array type definitions.
    ///
    /// Each WASM struct becomes a CLR class with typed fields:
    ///   (struct (field i32) (field f64)) → class WasmStruct_N { public int field_0; public double field_1; }
    ///
    /// Each WASM array becomes a CLR class wrapping a typed array:
    ///   (array (mut i32)) → class WasmArray_N { public int[] elements; public int length; }
    ///
    /// Structural type identity uses StableHash, not CLR type identity.
    /// Each emitted type carries a StructuralHash constant field.
    /// </summary>
    public class GcTypeEmitter
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly string _namespace;
        private readonly TypesSpace _types;
        private readonly Dictionary<int, EmittedGcType> _emittedTypes = new();

        public IReadOnlyDictionary<int, EmittedGcType> EmittedTypes => _emittedTypes;

        public GcTypeEmitter(ModuleBuilder moduleBuilder, string @namespace, TypesSpace types)
        {
            _moduleBuilder = moduleBuilder;
            _namespace = @namespace;
            _types = types;
        }

        /// <summary>
        /// Emit CLR types for all struct/array definitions in the module.
        /// Must be called before function emission (Pass 0).
        /// </summary>
        public void EmitTypes()
        {
            // First pass: create TypeBuilders (needed for forward references in ref-typed fields)
            for (int i = 0; _types.Contains((TypeIdx)i); i++)
            {
                var defType = _types[(TypeIdx)i];
                var expansion = defType.Expansion;

                if (expansion is StructType)
                {
                    var gcType = new EmittedGcType(i) { IsStruct = true };
                    gcType.TypeBuilder = _moduleBuilder.DefineType(
                        $"{_namespace}.WasmStruct_{i}",
                        TypeAttributes.Public | TypeAttributes.Class,
                        typeof(object),
                        new[] { typeof(IGcRef) });
                    _emittedTypes[i] = gcType;
                }
                else if (expansion is ArrayType)
                {
                    var gcType = new EmittedGcType(i) { IsStruct = false };
                    gcType.TypeBuilder = _moduleBuilder.DefineType(
                        $"{_namespace}.WasmArray_{i}",
                        TypeAttributes.Public | TypeAttributes.Class,
                        typeof(object),
                        new[] { typeof(IGcRef) });
                    _emittedTypes[i] = gcType;
                }
            }

            // Second pass: define fields (can now reference other emitted types)
            foreach (var kv in _emittedTypes)
            {
                var defType = _types[(TypeIdx)kv.Key];
                var expansion = defType.Expansion;
                var gcType = kv.Value;

                if (expansion is StructType structType)
                {
                    EmitStructFields(gcType, structType);
                }
                else if (expansion is ArrayType arrayType)
                {
                    EmitArrayFields(gcType, arrayType);
                }

                // Add structural hash as a static readonly field
                gcType.StructuralHash = defType.GetHashCode();
                var hashField = gcType.TypeBuilder!.DefineField(
                    "StructuralHash",
                    typeof(int),
                    FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
                hashField.SetConstant(gcType.StructuralHash);

                // Implement IGcRef.StoreIndex — returns a RefIdx for this instance.
                // For transpiled types, we use the type index as a stable identifier.
                EmitStoreIndexProperty(gcType);
            }

            // Third pass: create types (bake the TypeBuilders)
            foreach (var kv in _emittedTypes)
            {
                kv.Value.ClrType = kv.Value.TypeBuilder!.CreateType()!;
            }
        }

        private void EmitStructFields(EmittedGcType gcType, StructType structType)
        {
            var fields = new FieldBuilder[structType.Arity];
            for (int f = 0; f < structType.Arity; f++)
            {
                var fieldType = structType.FieldTypes[f];
                var clrFieldType = MapFieldType(fieldType);
                fields[f] = gcType.TypeBuilder!.DefineField(
                    $"field_{f}",
                    clrFieldType,
                    FieldAttributes.Public);
            }
            gcType.Fields = fields;
        }

        private void EmitArrayFields(EmittedGcType gcType, ArrayType arrayType)
        {
            var elementClrType = MapFieldType(arrayType.ElementType);
            var elementsField = gcType.TypeBuilder!.DefineField(
                "elements",
                elementClrType.MakeArrayType(),
                FieldAttributes.Public);
            var lengthField = gcType.TypeBuilder!.DefineField(
                "length",
                typeof(int),
                FieldAttributes.Public);
            gcType.Fields = new[] { elementsField, lengthField };
        }

        /// <summary>
        /// Emit the IGcRef.StoreIndex property implementation.
        /// Returns a PtrIdx with the type index as identifier.
        /// </summary>
        private static void EmitStoreIndexProperty(EmittedGcType gcType)
        {
            var tb = gcType.TypeBuilder!;

            // _storeIndex backing field
            var backingField = tb.DefineField("_storeIndex",
                typeof(PtrIdx), FieldAttributes.Private);

            // Property
            var prop = tb.DefineProperty("StoreIndex", PropertyAttributes.None, typeof(RefIdx), null);

            // Getter: return (RefIdx)_storeIndex
            var getter = tb.DefineMethod("get_StoreIndex",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName,
                typeof(RefIdx), Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, backingField);
            il.Emit(OpCodes.Box, typeof(PtrIdx));
            il.Emit(OpCodes.Ret);

            prop.SetGetMethod(getter);
        }

        /// <summary>
        /// Map a WASM field type to a CLR type.
        /// Packed i8/i16 → byte/short. Ref-typed fields optimistically use the emitted CLR type.
        /// </summary>
        private Type MapFieldType(FieldType fieldType)
        {
            return MapStorageType(fieldType.StorageType);
        }

        private Type MapStorageType(ValType storageType)
        {
            // Packed types
            if (storageType == ValType.I8) return typeof(byte);
            if (storageType == ValType.I16) return typeof(short);

            // Numeric types
            if (storageType == ValType.I32) return typeof(int);
            if (storageType == ValType.I64) return typeof(long);
            if (storageType == ValType.F32) return typeof(float);
            if (storageType == ValType.F64) return typeof(double);

            // Ref types — check if it's a concrete type index we've emitted
            if (storageType.IsDefType())
            {
                var idx = (int)storageType.Index().Value;
                if (_emittedTypes.TryGetValue(idx, out var gcType) && gcType.TypeBuilder != null)
                    return gcType.TypeBuilder;
            }

            // Everything else (funcref, externref, abstract heap types) → object
            return typeof(object);
        }

        /// <summary>
        /// Get the CLR type for a WASM type index, or null if not a struct/array.
        /// </summary>
        public Type? GetEmittedType(int typeIndex)
        {
            return _emittedTypes.TryGetValue(typeIndex, out var gcType) ? gcType.ClrType : null;
        }

        /// <summary>
        /// Get the EmittedGcType info for a type index.
        /// </summary>
        public EmittedGcType? GetGcType(int typeIndex)
        {
            return _emittedTypes.TryGetValue(typeIndex, out var gcType) ? gcType : null;
        }
    }
}
