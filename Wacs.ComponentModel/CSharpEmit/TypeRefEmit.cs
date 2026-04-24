// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Emits a C# type reference for a <see cref="CtValType"/>.
    /// Matches wit-bindgen-csharp's primitive mapping (confirmed
    /// against wit-bindgen 0.30.0 output for a function-per-primitive
    /// WIT).
    ///
    /// <para><b>Phase 1a.2 scope:</b> primitives only. Aggregates
    /// (<c>list&lt;T&gt;</c>, <c>option&lt;T&gt;</c>, <c>result&lt;T, E&gt;</c>,
    /// <c>tuple&lt;…&gt;</c>) and user-defined types
    /// (records, variants, enums, flags, resources, own/borrow)
    /// throw <see cref="NotImplementedException"/> — they ship in
    /// follow-up commits as the corresponding type emitters land.</para>
    /// </summary>
    internal static class TypeRefEmit
    {
        public static string Emit(CtValType type)
        {
            return type switch
            {
                CtPrimType p => EmitPrim(p.Kind),
                _ => throw new NotImplementedException(
                    "CSharp emission for " + type.GetType().Name +
                    " is a Phase 1a.2 follow-up."),
            };
        }

        /// <summary>
        /// Primitive mapping, verified against wit-bindgen-csharp 0.30.0:
        /// <list type="bullet">
        /// <item><description><c>bool</c> → <c>bool</c></description></item>
        /// <item><description><c>s8</c>/<c>u8</c> → <c>sbyte</c>/<c>byte</c></description></item>
        /// <item><description><c>s16</c>/<c>u16</c> → <c>short</c>/<c>ushort</c></description></item>
        /// <item><description><c>s32</c>/<c>u32</c> → <c>int</c>/<c>uint</c></description></item>
        /// <item><description><c>s64</c>/<c>u64</c> → <c>long</c>/<c>ulong</c></description></item>
        /// <item><description><c>f32</c>/<c>f64</c> → <c>float</c>/<c>double</c></description></item>
        /// <item><description><c>char</c> → <c>uint</c> (wit treats char as unsigned 32-bit Unicode scalar)</description></item>
        /// <item><description><c>string</c> → <c>string</c></description></item>
        /// </list>
        /// </summary>
        public static string EmitPrim(CtPrim kind) => kind switch
        {
            CtPrim.Bool => "bool",
            CtPrim.S8 => "sbyte",
            CtPrim.U8 => "byte",
            CtPrim.S16 => "short",
            CtPrim.U16 => "ushort",
            CtPrim.S32 => "int",
            CtPrim.U32 => "uint",
            CtPrim.S64 => "long",
            CtPrim.U64 => "ulong",
            CtPrim.F32 => "float",
            CtPrim.F64 => "double",
            CtPrim.Char => "uint",
            CtPrim.String => "string",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
