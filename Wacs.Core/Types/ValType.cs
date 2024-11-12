// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.IO;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.4 Value Types
    /// Represents the value types used in WebAssembly.
    /// </summary>
    public enum ValType : byte
    {
        // =========================
        // Numeric Types
        // =========================

        /// <summary>
        /// 32-bit integer
        /// </summary>
        [WatToken("i32")] I32 = 0x7F,

        /// <summary>
        /// 64-bit integer
        /// </summary>
        [WatToken("i64")] I64 = 0x7E,

        /// <summary>
        /// 32-bit floating point
        /// </summary>
        [WatToken("f32")] F32 = 0x7D,

        /// <summary>
        /// 64-bit floating point
        /// </summary>
        [WatToken("f64")] F64 = 0x7C,

        // =========================
        // Vector Types (SIMD)
        // =========================

        /// <summary>
        /// 128-bit vector
        /// </summary>
        [WatToken("v128")] V128 = 0x7B, // (SIMD extension)

        // =========================
        // Reference Types
        // =========================

        /// <summary>
        /// Function reference
        /// </summary>
        [WatToken("funcref")] Funcref = 0x70,

        /// <summary>
        /// External reference
        /// </summary>
        [WatToken("externref")] Externref = 0x6F,

        // =========================
        // Future Types
        // =========================

        // Additional types from future extensions can be added here.
        
        
        //Special types
        
        [WatToken("Unknown")] Unknown = 0xFC, //for validation
        
        ExecContext = 0xFD,
        Nil = 0xFE,
        Undefined = 0xFF,
    }

    public static class ValueTypeExtensions
    {
        public static bool IsCompatible(this ValType left, ValType right) => 
            left == right || left == ValType.Unknown || right == ValType.Unknown;

        public static bool IsNumeric(this ValType type) => type switch {
            ValType.I32 => true,
            ValType.I64 => true,
            ValType.F32 => true,
            ValType.F64 => true,
            _ => false
        };

        public static bool IsInteger(this ValType type) => type switch {
            ValType.I32 => true,
            ValType.I64 => true,
            _ => false
        };

        public static bool IsFloat(this ValType type) => type switch {
            ValType.F32 => true,
            ValType.F64 => true,
            _ => false
        };

        public static bool IsVector(this ValType type) => type == ValType.V128;

        public static bool IsReference(this ValType type) => type switch {
            ValType.Funcref => true,
            ValType.Externref => true,
            _ => false
        };

        public static ResultType SingleResult(this ValType type) => new(type);
    }

    public static class ValTypeUtilities
    {
        public static ValType ToValType(this Type type) =>
            type switch {
                { } t when t == typeof(sbyte) => ValType.I32,
                { } t when t == typeof(byte) => ValType.I32,
                { } t when t == typeof(char) => ValType.I32,
                { } t when t == typeof(short) => ValType.I64,
                { } t when t == typeof(ushort) => ValType.I64,
                { } t when t == typeof(int) => ValType.I32,
                { } t when t == typeof(uint) => ValType.I32,
                { } t when t == typeof(long) => ValType.I64,
                { } t when t == typeof(ulong) => ValType.I64,
                { } t when t == typeof(float) => ValType.F32,
                { } t when t == typeof(double) => ValType.F64,
                { } t when t == typeof(void) => ValType.Nil,
                { } t when t == typeof(ExecContext) => ValType.ExecContext,
                { } t when t.GetWasmType() is { } wasmType => wasmType,
                // { } t when t == typeof(System.Numerics.Vector128<byte>) ||
                //            t == typeof(System.Numerics.Vector128<float>) ||
                //            t == typeof(System.Numerics.Vector128<int>) ||
                //            t == typeof(System.Numerics.Vector128<long>) => ValType.V128,
                _ => throw new InvalidCastException($"Unsupported type: {type.FullName}")
            };

        public static ValType UnpackRef(Type type) => 
            type.IsByRef ? type.GetElementType()?.ToValType() ?? ValType.Nil : type.ToValType();
    }

    public static class ValTypeParser
    {
        public static ValType Parse(BinaryReader reader) =>
            // _ handles Undefined should throw
            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            (ValType)reader.ReadByte() switch {
                //Numeric Types
                ValType.I32 => ValType.I32,
                ValType.I64 => ValType.I64,
                ValType.F32 => ValType.F32,
                ValType.F64 => ValType.F64,
                //Vector Types (SIMD)
                ValType.V128 => ValType.V128,
                //Reference Types
                ValType.Funcref => ValType.Funcref,
                ValType.Externref => ValType.Externref,
                _ => throw new FormatException($"Invalid value type at offset {reader.BaseStream.Position}.")
            };
    }
}