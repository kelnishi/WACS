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

using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.Core
{
    /// <summary>
    /// All the abstract type encodings fit within the byte/s33 format as negative numbers.
    /// The spec allows for 2^32 concrete types (indices), but we'll just do 2^31 for practicality. 
    /// </summary>
    public enum ScalarType : int
    {
        [WatToken("i32")]      I32          = unchecked((sbyte)(0x80|NumType.I32)),             // -0x01
        [WatToken("i64")]      F32          = unchecked((sbyte)(0x80|NumType.F32)),             // -0x02
        [WatToken("f32")]      I64          = unchecked((sbyte)(0x80|NumType.I64)),             // -0x03
        [WatToken("f64")]      F64          = unchecked((sbyte)(0x80|NumType.F64)),             // -0x04
        [WatToken("v128")]     V128         = unchecked((sbyte)(0x80|VecType.V128)),            // -0x05
        
        [WatToken("u32")]      U32 = -0x06, // not in spec
        [WatToken("u64")]      U64 = -0x07, // not in spec
        
        //Aggregate Types
        I8                                  = unchecked((sbyte)(0x80|PackedType.I8)),           // -0x08
        I16                                 = unchecked((sbyte)(0x80|PackedType.I16)),          // -0x09
        
        //Reference Types
        [WatToken("nofunc")]   NoFunc       = unchecked((sbyte)(0x80|HeapType.NoFunc)),         // -0x0d
        [WatToken("noextern")] NoExtern     = unchecked((sbyte)(0x80|HeapType.NoExtern)),       // -0x0e
        [WatToken("none")]     None         = unchecked((sbyte)(0x80|HeapType.None)),           // -0x0f
        [WatToken("func")]     Func         = unchecked((sbyte)(0x80|HeapType.Func)),           // -0x10
        [WatToken("extern")]   Extern       = unchecked((sbyte)(0x80|HeapType.Extern)),         // -0x11
        [WatToken("any")]      Any          = unchecked((sbyte)(0x80|HeapType.Any)),            // -0x12
        [WatToken("eq")]       Eq           = unchecked((sbyte)(0x80|HeapType.Eq)),             // -0x13
        [WatToken("i31")]      I31          = unchecked((sbyte)(0x80|HeapType.I31)),            // -0x14
        [WatToken("struct")]   Struct       = unchecked((sbyte)(0x80|HeapType.Struct)),         // -0x15
        [WatToken("array")]    Array        = unchecked((sbyte)(0x80|HeapType.Array)),          // -0x16
           
        [WatToken("ref ht")]   RefHt        = unchecked((sbyte)(0x80|TypePrefix.RefHt)),        // -0x1c
        [WatToken("ref null ht")] RefNullHt = unchecked((sbyte)(0x80|TypePrefix.RefNullHt)),    // -0x1d
        
        Empty                               = unchecked((sbyte)(0x80|TypePrefix.EmptyBlock)),   // -0xc0
        
        //Recursive Types
        RecSt                               = unchecked((sbyte)(0x80|RecType.RecSt)),           // -0xce
        SubXCt                              = unchecked((sbyte)(0x80|RecType.SubXCt)),          // -0xcf
        SubFinalXCt                         = unchecked((sbyte)(0x80|RecType.SubFinalXCt)),     // -0xd0
        
        //Composite Types
        ArrayAt                             = unchecked((sbyte)(0x80|CompType.ArrayAt)),        // -0xde
        Structst                            = unchecked((sbyte)(0x80|CompType.StructSt)),       // -0xdF
        FuncFt                              = unchecked((sbyte)(0x80|CompType.FuncFt)),         // -0xe0
    }
}