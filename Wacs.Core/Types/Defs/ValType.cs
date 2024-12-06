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
using Wacs.Core.Utilities;

namespace Wacs.Core.Types.Defs
{
    /// <summary>
    /// @Spec 2.3.4 Value Types
    /// Represents the value types used in WebAssembly.
    ///
    /// All the abstract type encodings fit within the byte/s33 format as negative numbers.
    /// The spec allows for 2^32 concrete types (indices), but we'll just do 2^30 for practicality. 
    /// </summary>
    [Flags]
    public enum ValType : int
    {
        [WatToken("i32")]  I32               = unchecked((sbyte)(0x80|NumType.I32)),             // -0x01
        [WatToken("f32")]  F32               = unchecked((sbyte)(0x80|NumType.F32)),             // -0x02
        [WatToken("i64")]  I64               = unchecked((sbyte)(0x80|NumType.I64)),             // -0x03
        [WatToken("f64")]  F64               = unchecked((sbyte)(0x80|NumType.F64)),             // -0x04
        [WatToken("v128")] V128              = unchecked((sbyte)(0x80|VecType.V128)),            // -0x05
        
        [WatToken("u32")]  U32               = -0x06, // not in spec
        [WatToken("u64")]  U64               = -0x07, // not in spec
        
        //Aggregate Types
        I8                                   = unchecked((sbyte)(0x80|PackedType.I8)),           // -0x08
        I16                                  = unchecked((sbyte)(0x80|PackedType.I16)),          // -0x09
        
        //Host Reference
        Host                                 = -0x0c,
        
        //Reference Types
        [WatToken("nullfuncref")]   NoFunc   = unchecked((sbyte)(0x80|HeapType.NoFunc)),         // -0x0d
        [WatToken("nullexternref")] NoExtern = unchecked((sbyte)(0x80|HeapType.NoExtern)),       // -0x0e
        [WatToken("nullref")]       None     = unchecked((sbyte)(0x80|HeapType.None)),           // -0x0f
        [WatToken("funcref")]       Func     = unchecked((sbyte)(0x80|HeapType.Func)),           // -0x10
        [WatToken("externref")]     Extern   = unchecked((sbyte)(0x80|HeapType.Extern)),         // -0x11
        [WatToken("anyref")]        Any      = unchecked((sbyte)(0x80|HeapType.Any)),            // -0x12
        [WatToken("eqref")]         Eq       = unchecked((sbyte)(0x80|HeapType.Eq)),             // -0x13
        [WatToken("i31ref")]        I31      = unchecked((sbyte)(0x80|HeapType.I31)),            // -0x14
        [WatToken("structref")]     Struct   = unchecked((sbyte)(0x80|HeapType.Struct)),         // -0x15
        [WatToken("arrayref")]      Array    = unchecked((sbyte)(0x80|HeapType.Array)),          // -0x16
           
        [WatToken("ref ht")]      RefHt      = unchecked((sbyte)(0x80|TypePrefix.RefHt)),        // -0x1c
        [WatToken("ref null ht")] RefNullHt  = unchecked((sbyte)(0x80|TypePrefix.RefNullHt)),    // -0x1d
        
        Empty                                = unchecked((sbyte)(0x80|TypePrefix.EmptyBlock)),   // -0xc0
        
        //Recursive Types
        RecSt                                = unchecked((sbyte)(0x80|RecType.RecSt)),           // -0xce
        SubXCt                               = unchecked((sbyte)(0x80|RecType.SubXCt)),          // -0xcf
        SubFinalXCt                          = unchecked((sbyte)(0x80|RecType.SubFinalXCt)),     // -0xd0
        
        //Composite Types
        ArrayAt                              = unchecked((sbyte)(0x80|CompType.ArrayAt)),        // -0xde
        Structst                             = unchecked((sbyte)(0x80|CompType.StructSt)),       // -0xdf
        FuncFt                               = unchecked((sbyte)(0x80|CompType.FuncFt)),         // -0xe0
        
        SignBit = unchecked((int)0x8000_0000),
        NullBit = 0x4000_0000,
        
        //NonNullable (unset null bit)
        [WatToken("ref nofunc")]   NoFuncNN   = NoFunc & ~NullBit,  
        [WatToken("ref noextern")] NoExternNN = NoExtern & ~NullBit,
        [WatToken("ref none")]     NoneNN     = None & ~NullBit,    
        [WatToken("ref func")]     FuncNN     = Func & ~NullBit,    
        [WatToken("ref extern")]   ExternNN   = Extern & ~NullBit,  
        [WatToken("ref any")]      AnyNN      = Any & ~NullBit,     
        [WatToken("ref eq")]       EqNN       = Eq & ~NullBit,      
        [WatToken("ref i31")]      I31NN      = I31 & ~NullBit,     
        [WatToken("ref struct")]   StructNN   = Struct & ~NullBit,  
        [WatToken("ref array")]    ArrayNN    = Array & ~NullBit,
        
        //for validation
        Undefined = SignBit | NullBit,
        Nil = Undefined + 1,
        ExecContext = Undefined + 2,
        [WatToken("Unknown")] Unknown = Undefined + 3, 
    }


    public static class ValueTypeExtensions
    {
        private const uint NNMask = ~(uint)0x4000_0000;

        public static TypeIdx Index(this ValType type) => 
            (TypeIdx)(NNMask & (uint)type);

        public static bool IsNullable(this ValType type) => (type & ValType.NullBit) != 0;

        public static bool IsNull(this ValType type) => type switch
        {
            ValType.None => true,
            ValType.NoFunc => true,
            ValType.NoExtern => true,
            _ => false,
        };

        public static HeapType GetHeapType(this ValType type)
        {
            return type switch
            {
                ValType.NoFunc => HeapType.NoFunc,
                ValType.NoExtern => HeapType.NoExtern,
                ValType.None => HeapType.None,
                ValType.Func => HeapType.Func,
                ValType.Extern => HeapType.Extern,
                ValType.Any => HeapType.Any,
                ValType.Eq => HeapType.Eq,
                ValType.I31 => HeapType.I31,
                ValType.Struct => HeapType.Struct,
                ValType.Array => HeapType.Array,
                ValType.NoFuncNN => HeapType.NoFunc,
                ValType.NoExternNN => HeapType.NoExtern,
                ValType.NoneNN => HeapType.None,
                ValType.FuncNN => HeapType.Func,
                ValType.ExternNN => HeapType.Extern,
                ValType.AnyNN => HeapType.Any,
                ValType.EqNN => HeapType.Eq,
                ValType.I31NN => HeapType.I31,
                ValType.StructNN => HeapType.Struct,
                ValType.ArrayNN => HeapType.Array,
                _ => (HeapType)0
            };
        }

        public static bool IsRefType(this ValType type)
        {
            return type switch
            {
                ValType.NoFunc => true,
                ValType.NoExtern => true,
                ValType.None => true,
                ValType.Func => true,
                ValType.Extern => true,
                ValType.Any => true,
                ValType.Eq => true,
                ValType.I31 => true,
                ValType.Struct => true,
                ValType.Array => true,
                ValType.NoFuncNN => true,  
                ValType.NoExternNN => true,
                ValType.NoneNN => true,    
                ValType.FuncNN => true,    
                ValType.ExternNN => true,  
                ValType.AnyNN => true,     
                ValType.EqNN => true,      
                ValType.I31NN => true,     
                ValType.StructNN => true,  
                ValType.ArrayNN => true,   
                _ when (int)type >= 0 => true,
                _ => false
            };
        }

        public static bool IsSubType(this ValType type, ValType ofType)
        {
            return type.GetHeapType() switch
            {
                HeapType.NoFunc => ofType switch {
                    ValType.Func => true,
                    _ => false
                },
                HeapType.NoExtern => ofType switch {
                    ValType.Extern => true,
                    _ => false
                },
                HeapType.None => ofType switch {
                    ValType.I31 => true,
                    ValType.Array => true,
                    ValType.Struct => true,
                    _ => ValType.I31.IsSubType(ofType) || ValType.Array.IsSubType(ofType) || ValType.Struct.IsSubType(ofType)  
                },
                HeapType.Array => ofType switch {
                    ValType.Eq => true,
                    _ => ValType.Eq.IsSubType(ofType)
                },
                HeapType.Struct => ofType switch {
                    ValType.Eq => true,
                    _ => ValType.Eq.IsSubType(ofType)
                },
                HeapType.I31 => ofType switch {
                    ValType.Eq => true,
                    _ => ValType.Eq.IsSubType(ofType)
                },
                HeapType.Eq => ofType switch {
                    ValType.Any => true,
                    _ => false,
                },
                HeapType.Any => true,
                _ => false
            };
        }

        public static bool IsCompatible(this ValType left, ValType right)
        {
            if (left == right || left == ValType.Unknown || right == ValType.Unknown)
                return true;
            
            if (right.IsRefType())
            {
                if (left.IsSubType(right))
                    return true;
                if (right.IsNullable() && left.IsNull())
                    return true;
            }

            return false;
        }
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
                { } t when t == typeof(V128) => ValType.V128,
                { } t when t == typeof(void) => ValType.Nil,
                { } t when t == typeof(ExecContext) => ValType.ExecContext,
                { } t when t.GetWasmType() is { } wasmType => wasmType,
                _ => throw new InvalidCastException($"Unsupported type: {type.FullName}")
            };

        public static ValType UnpackRef(Type type) => 
            type.IsByRef ? type.GetElementType()?.ToValType() ?? ValType.Nil : type.ToValType();


        public static string ToNotation(this ValType type) =>
            Enum.IsDefined(typeof(ValType), type)
                ? type.ToWat()
                : type.Index().Value switch {
                    -1 => "ref.null",
                    var idx => $"ref {idx}"
                };
    }

    public static class ValTypeParser
    {
        public static ValType ParseHeapType(BinaryReader reader)
        {
            byte token = reader.ReadByte();
            return token switch
            {
                (byte)HeapType.NoFunc => ValType.NoFunc,
                (byte)HeapType.NoExtern => ValType.NoExtern,
                (byte)HeapType.None => ValType.None,
                (byte)HeapType.Func => ValType.Func,
                (byte)HeapType.Extern => ValType.Extern,
                (byte)HeapType.Any => ValType.Any,
                (byte)HeapType.Eq => ValType.Eq,
                (byte)HeapType.I31 => ValType.I31,
                (byte)HeapType.Struct => ValType.Struct,
                (byte)HeapType.Array => ValType.Array,
                //Index
                _ => (ValType)reader.ContinueReading_s33(token), 
            };
        }

        public static ValType ParseRefType(BinaryReader reader)
        {
            var type = Parse(reader, true, false);
            if (!type.IsRefType())
                throw new FormatException($"Type is not a Reference Type:{type}");
            return type;
        }

        public static ValType Parse(BinaryReader reader) => Parse(reader, false, false);

        public static ValType Parse(BinaryReader reader, bool parseBlockIndex, bool parseStorageType)
        {
            long pos = reader.BaseStream.Position;
            byte token = reader.ReadByte();
            return token switch
            {
                //Numeric Types
                (byte)NumType.I32 => ValType.I32,
                (byte)NumType.I64 => ValType.I64,
                (byte)NumType.F32 => ValType.F32,
                (byte)NumType.F64 => ValType.F64,
                //Vector Types (SIMD)
                (byte)VecType.V128 => ValType.V128,
                //Reference Types (nullable abstract)
                (byte)HeapType.NoFunc => ValType.NoFunc,
                (byte)HeapType.NoExtern => ValType.NoExtern,
                (byte)HeapType.None => ValType.None,
                (byte)HeapType.Func => ValType.Func,
                (byte)HeapType.Extern => ValType.Extern,
                (byte)HeapType.Any => ValType.Any,
                (byte)HeapType.Eq => ValType.Eq,
                (byte)HeapType.I31 => ValType.I31,
                (byte)HeapType.Struct => ValType.Struct,
                (byte)HeapType.Array => ValType.Array,
                //Abstract or Index
                (byte)TypePrefix.RefHt => ParseHeapType(reader) & ~ValType.NullBit,   //non-nullable
                (byte)TypePrefix.RefNullHt => ParseHeapType(reader),                  //nullable
                
                //StorageType
                (byte)PackedType.I8 when parseStorageType => ValType.I8,
                (byte)PackedType.I16 when parseStorageType => ValType.I16,

                //Blocks
                (byte)TypePrefix.EmptyBlock when parseBlockIndex => ValType.Empty,
                //Parse an index
                _ when parseBlockIndex => (ValType)reader.ContinueReading_s33(token),

                var b => throw new FormatException($"Invalid value type {b:X} at offset 0x{pos:X}.")
            };
        }
    }
}