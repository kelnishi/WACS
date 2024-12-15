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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        //Reserve 3 bits for admin
        //indicies are limited to 2^29
        SignBit = unchecked((int)0x8000_0000),
        [WatToken("ref")] Ref = 0x4000_0000, //Naturally set, unset for valtypes
        Nullable = 0x2000_0000, //Naturally set for any negative keys set below
        [WatToken("ref null")] NullableRef = Ref | Nullable,
        IndexMask = ~NullableRef,
        
        [WatToken("i32")]  I32               = (int)((uint)NumType.I32 | SignBit),    // -0x01
        [WatToken("f32")]  F32               = (int)((uint)NumType.F32 | SignBit),    // -0x02
        [WatToken("i64")]  I64               = (int)((uint)NumType.I64 | SignBit),    // -0x03
        [WatToken("f64")]  F64               = (int)((uint)NumType.F64 | SignBit),    // -0x04
        [WatToken("v128")] V128              = (int)((uint)VecType.V128 | SignBit),   // -0x05
        
        [WatToken("u32")]  U32               = (int)(unchecked((uint)-0x06) & 0xFF | SignBit), // not in spec
        [WatToken("u64")]  U64               = (int)(unchecked((uint)-0x07) & 0xFF | SignBit), // not in spec
        
        //Packed Types
        I8                                   = (int)((uint)PackedType.I8 | SignBit),  // -0x08
        I16                                  = (int)((uint)PackedType.I16 | SignBit), // -0x09
        
        //Host Reference
        Host                                 = (int)(unchecked((uint)-0x0c) & 0xFF | NullableRef),
        
        //Reference Types
        [WatToken("nullfuncref")]   NoFunc   = (int)((uint)HeapType.NoFunc | NullableRef | SignBit),          // -0x0d
        [WatToken("nullexternref")] NoExtern = (int)((uint)HeapType.NoExtern | NullableRef | SignBit),        // -0x0e
        [WatToken("nullref")]       None     = (int)((uint)HeapType.None | NullableRef | SignBit),            // -0x0f
        [WatToken("funcref")]       FuncRef     = (int)((uint)HeapType.Func | NullableRef | SignBit),            // -0x10
        [WatToken("externref")]     ExternRef   = (int)((uint)HeapType.Extern | NullableRef | SignBit),          // -0x11
        [WatToken("anyref")]        Any      = (int)((uint)HeapType.Any | NullableRef | SignBit),             // -0x12
        [WatToken("eqref")]         Eq       = (int)((uint)HeapType.Eq | NullableRef | SignBit),              // -0x13
        [WatToken("i31ref")]        I31      = (int)((uint)HeapType.I31 | NullableRef | SignBit),             // -0x14
        [WatToken("structref")]     Struct   = (int)((uint)HeapType.Struct | NullableRef | SignBit),          // -0x15
        [WatToken("arrayref")]      Array    = (int)((uint)HeapType.Array | NullableRef | SignBit),           // -0x16
           
        [WatToken("ref ht")]        RefHt    = (int)((uint)TypePrefix.RefHt | SignBit),         // -0x1c
        [WatToken("ref null ht")]   RefNullHt= (int)((uint)TypePrefix.RefNullHt | SignBit),     // -0x1d
        
        Empty                                = (int)((uint)TypePrefix.EmptyBlock | SignBit),   // -0xc0
        
        //Recursive Types
        RecSt                                = (int)((uint)RecType.RecSt | SignBit),            // -0xce
        SubXCt                               = (int)((uint)RecType.SubXCt | SignBit),           // -0xcf
        SubFinalXCt                          = (int)((uint)RecType.SubFinalXCt | SignBit),      // -0xd0
        
        //Composite Types
        ArrayAt                              = (int)((uint)CompType.ArrayAt | SignBit),         // -0xde
        Structst                             = (int)((uint)CompType.StructSt | SignBit),        // -0xdf
        FuncFt                               = (int)((uint)CompType.FuncFt | SignBit),          // -0xe0
        
        
        //NonNullable (unset null bit)
        [WatToken("ref nofunc")]   NoFuncNN   = (int)((uint)HeapType.NoFunc | Ref | SignBit),  
        [WatToken("ref noextern")] NoExternNN = (int)((uint)HeapType.NoExtern | Ref | SignBit),
        [WatToken("ref none")]     NoneNN     = (int)((uint)HeapType.None | Ref | SignBit),    
        [WatToken("ref func")]     Func     = (int)((uint)HeapType.Func | Ref | SignBit),    
        [WatToken("ref extern")]   Extern   = (int)((uint)HeapType.Extern | Ref | SignBit),  
        [WatToken("ref any")]      AnyNN      = (int)((uint)HeapType.Any | Ref | SignBit),     
        [WatToken("ref eq")]       EqNN       = (int)((uint)HeapType.Eq | Ref | SignBit),      
        [WatToken("ref i31")]      I31NN      = (int)((uint)HeapType.I31 | Ref | SignBit),     
        [WatToken("ref struct")]   StructNN   = (int)((uint)HeapType.Struct | Ref | SignBit),  
        [WatToken("ref array")]    ArrayNN    = (int)((uint)HeapType.Array | Ref | SignBit),   
        
        //for validation
        [WatToken("Bot")] Bot         = (int)((uint)HeapType.Bot | Ref | SignBit), 
        Undefined                     = 0x02 << 8 | SignBit,
        Nil                           = 0x03 << 8 | SignBit,
        ExecContext                   = 0x04 << 8 | SignBit,
    }

    public class Wat
    {
        public static explicit operator Wat(ValType type) => new Wat(type);

        private ValType _type;
        private Wat(ValType type) => _type = type;
                 
        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("(");
            if (_type.IsDefType())
            {
                if (_type.IsRefType())
                    sb.Append("ref");
                if (_type.IsNullable())
                    sb.Append(" null");
                sb.Append($" ${_type.Index().Value}");
            }
            else
                sb.Append(_type.ToWat());
            sb.Append(")");
            return sb.ToString();
        }
    }

    public static class ValueTypeExtensions
    {

        public static string ToString(this ValType type) =>
            Enum.IsDefined(typeof(ValType), type) ? type.ToWat() : $"ValType({type:X})";
        
        public static TypeIdx Index(this ValType type) => 
            (TypeIdx)((int)type & (int)ValType.IndexMask);

        public static bool IsDefType(this ValType type) => type.Index().Value >= 0;
        
        public static bool IsNullable(this ValType type) => (type & ValType.Nullable) != 0;

        public static bool IsNull(this ValType type) => type switch
        {
            ValType.None => true,
            ValType.NoFunc => true,
            ValType.NoExtern => true,
            ValType.Bot => true,
            _ => false,
        };

        public static bool IsVal(this ValType type) => type switch
        {
            ValType.I32 
                or ValType.I64 
                or ValType.F32 
                or ValType.F64 
                or ValType.V128
                or ValType.Bot => true,
            _ => false
        };

        public static bool IsDefaultable(this ValType type) => 
            !type.IsRefType() || type.IsNullable();

        public static bool IsPacked(this ValType type) => type switch
        {
            ValType.I8 or ValType.I16 => true,
            _ => false
        };

        public static HeapType GetHeapType(this ValType type)
        {
            return type switch
            {
                ValType.NoFunc => HeapType.NoFunc,
                ValType.NoExtern => HeapType.NoExtern,
                ValType.None => HeapType.None,
                ValType.FuncRef => HeapType.Func,
                ValType.ExternRef => HeapType.Extern,
                ValType.Any => HeapType.Any,
                ValType.Eq => HeapType.Eq,
                ValType.I31 => HeapType.I31,
                ValType.Struct => HeapType.Struct,
                ValType.Array => HeapType.Array,
                ValType.NoFuncNN => HeapType.NoFunc,
                ValType.NoExternNN => HeapType.NoExtern,
                ValType.NoneNN => HeapType.None,
                ValType.Func => HeapType.Func,
                ValType.Extern => HeapType.Extern,
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
            return (type & ValType.Ref) != 0;
        }

        public static ValType TopHeapType(this ValType heapType, TypesSpace types) =>
            heapType switch
            {
                ValType.Any or ValType.Eq or ValType.I31 or ValType.Struct or ValType.Array or ValType.None =>
                    ValType.Any,
                ValType.FuncRef or ValType.NoFunc => 
                    ValType.FuncRef,
                ValType.ExternRef or ValType.NoExtern =>
                    ValType.ExternRef,
                ValType.AnyNN or ValType.EqNN or ValType.I31NN or ValType.StructNN or ValType.ArrayNN or ValType.NoneNN =>
                    ValType.AnyNN,
                _ when heapType.Index().Value >= 0 => types[heapType.Index()].Expansion switch {
                    StructType or ArrayType => ValType.Any,
                    FunctionType => ValType.FuncRef,
                    var type => throw new InvalidDataException($"Unknown DefType[{heapType.Index().Value}]: {type}"),
                },
                ValType.Bot or _ => throw new Exception($"HeapType {heapType} cannot occur in source"),
            };
        
        public static bool Validate(this ValType type, TypesSpace? types) =>
            type switch
            {
                ValType.I32 or 
                    ValType.I64 or 
                    ValType.F32 or 
                    ValType.F64 or 
                    ValType.V128 or
                    ValType.NoFunc or 
                    ValType.NoExtern or 
                    ValType.None or 
                    ValType.FuncRef or 
                    ValType.ExternRef or
                    ValType.Any or
                    ValType.Eq or 
                    ValType.I31 or 
                    ValType.Struct or 
                    ValType.Array => true,
                ValType.NoFuncNN or  
                    ValType.NoExternNN or
                    ValType.NoneNN or    
                    ValType.Func or    
                    ValType.Extern or  
                    ValType.AnyNN or     
                    ValType.EqNN or      
                    ValType.I31NN or     
                    ValType.StructNN or  
                    ValType.ArrayNN => true, 
                _ when type.IsDefType() => types.Contains(type.Index()),
                _ => false
            };

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#heap-types⑤
        /// </summary>
        /// <param name="type"></param>
        /// <param name="ofType"></param>
        /// <param name="types"></param>
        /// <returns></returns>
        public static bool IsSubType(this ValType type, ValType ofType, TypesSpace? types)
        {
            if (type == ofType)
                return true;
            
            var typeHt = type.GetHeapType();
            var ofTypeHt = ofType.GetHeapType();

            if (type.IsDefType())
            {
                var typeDef = types?[type.Index()];
                if (ofType.IsDefType())
                {
                    var ofTypeDef = types?[ofType.Index()];
                    return typeDef!.Matches(ofTypeDef!, types);
                }
                return typeDef!.Expansion switch
                {
                    StructType => ofType == ValType.Struct || ValType.Struct.IsSubType(ofType, types),
                    ArrayType => ofType == ValType.Array || ValType.Array.IsSubType(ofType, types),
                    FunctionType => ofType == ValType.Func || ValType.Func.IsSubType(ofType, types),
                    _ => throw new InvalidDataException($"Unknown CompositeType: {typeDef.Expansion}")
                };
            }

            return typeHt switch
            {
                var ht when ht == ofTypeHt => true,
                HeapType.Eq when ofTypeHt == HeapType.Any => true,
                HeapType.I31 or HeapType.Struct or HeapType.Array => 
                    ofType == ValType.Eq || ValType.Eq.IsSubType(ofType, types),
                HeapType.None => ofType.IsSubType(ValType.Any, types),
                HeapType.NoFunc => ofType.IsSubType(ValType.FuncRef, types),
                HeapType.NoExtern => ofType.IsSubType(ValType.ExternRef, types),
                HeapType.Bot => true,
                _ => false
            };
        }

        public static ValType AsNonNullable(this ValType type) => type & ~ValType.Nullable;
        public static ValType AsNullable(this ValType type) => type | ValType.Nullable;

        public static ValType AsDiff(this ValType type, ValType other, TypesSpace types)
        {
            //Subtypes differ in only super types
            return type.Matches(other, types)
                //But, the difference set could be all the nullables
                ? other.IsNullable() && !type.IsNullable() ? other : ValType.Bot
                //Disjoint types are just the other
                : other;
        }

        //For matching a (nullable?) Value directly
        public static bool Matches(this ValType rt1, Value refVal, TypesSpace types)
        {
            if (rt1 is ValType.Any)
                return true;
            if (refVal.IsNullRef)
            {
                if (rt1.IsNullable() || rt1 is ValType.None or ValType.NoneNN)
                    return true;
                if (refVal.Type is ValType.FuncRef or ValType.NoFunc && rt1 is ValType.NoFunc or ValType.NoFuncNN)
                    return true;
                if (refVal.Type is ValType.ExternRef or ValType.NoExtern && rt1 is ValType.NoExtern or ValType.NoExternNN)
                    return true;

                return refVal.Type.Matches(rt1, types);
            }
            else
            {
                if (rt1 is ValType.None or ValType.NoneNN or ValType.NoFunc or ValType.NoFuncNN or ValType.NoExtern or ValType.NoExternNN)
                    return false;
                if (rt1 is ValType.AnyNN)
                    return true;

                var concreteType = refVal.Type.AsNonNullable();
                return concreteType.Matches(rt1, types);
            }
        }

        public static bool Matches(this ValType left, ValType right, TypesSpace? types)
        {
            if (right == ValType.Any)
                return true;
            if (right == ValType.AnyNN && !left.IsNullable())
                return true;
            if (left == ValType.Bot || right == ValType.Bot)
                return true;
            
            if (left.IsRefType() && right.IsRefType())
            {
                //Check the HeapType
                //Non-nullable cannot receive nullable
                return left.IsSubType(right, types) && (!left.IsNullable() || right.IsNullable());
            }

            return left == right;
        }

        public static int ComputeHash(this ValType type, int defIndexValue, List<DefType> defs)
        {
            var hash = new StableHash();
            hash.Add(nameof(ValType));
            if (type.IsDefType())
            {
                if (type.Index().Value >= defs.Count)
                    throw new FormatException($"Types cannot forward-declare beyond the recursive block.");
                
                int index = type.Index().Value - defIndexValue;
                if (index < 0)
                {
                    index = defs[type.Index().Value].GetHashCode();
                }
                hash.Add(index);
            }
            else
            {
                hash.Add(type);
            }
            return hash.ToHashCode();
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
        public static ValType ParseHeapType(BinaryReader reader, bool nullable)
        {
            byte token = reader.ReadByte();
            var type = token switch
            {
                (byte)HeapType.NoFunc => ValType.NoFunc,
                (byte)HeapType.NoExtern => ValType.NoExtern,
                (byte)HeapType.None => ValType.None,
                (byte)HeapType.Func => ValType.FuncRef,
                (byte)HeapType.Extern => ValType.ExternRef,
                (byte)HeapType.Any => ValType.Any,
                (byte)HeapType.Eq => ValType.Eq,
                (byte)HeapType.I31 => ValType.I31,
                (byte)HeapType.Struct => ValType.Struct,
                (byte)HeapType.Array => ValType.Array,
                //Index
                _ => (ValType)reader.ContinueReading_s33(token), 
            };
            return nullable
                ? type | ValType.Ref | ValType.Nullable
                : (type | ValType.Ref) & ~ValType.Nullable;
        }

        public static ValType ParseRefType(BinaryReader reader)
        {
            var type = Parse(reader, true, false);
            if (!type.IsRefType())
                throw new FormatException($"Type is not a Reference Type:{type}");
            return type;
        }
        
        //For parsing unrolled recursive (module-level) type indexes
        public static ValType ParseDefType(BinaryReader reader) => 
            Parse(reader, true, false);

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
                (byte)HeapType.Func => ValType.FuncRef,
                (byte)HeapType.Extern => ValType.ExternRef,
                (byte)HeapType.Any => ValType.Any,
                (byte)HeapType.Eq => ValType.Eq,
                (byte)HeapType.I31 => ValType.I31,
                (byte)HeapType.Struct => ValType.Struct,
                (byte)HeapType.Array => ValType.Array,
                //Abstract or Index (set ref bit)
                (byte)TypePrefix.RefHt => ParseHeapType(reader, false),   //non-nullable
                (byte)TypePrefix.RefNullHt => ParseHeapType(reader, true),  //nullable
                
                //StorageType
                (byte)PackedType.I8 when parseStorageType => ValType.I8,
                (byte)PackedType.I16 when parseStorageType => ValType.I16,

                //Blocks
                (byte)TypePrefix.EmptyBlock when parseBlockIndex => ValType.Empty,
                //Parse an index
                _ when parseBlockIndex => (ValType)reader.ContinueReading_s33(token), //Not a natural reference

                var b => throw new FormatException($"Invalid value type {b:X} at offset 0x{pos:X}.")
            };
        }
    }
}