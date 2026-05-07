// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Primitive-type lift/lower expression builders shared by the
    /// import Interop, export Trampoline, and resource-method
    /// emitters. Mirrors wit-bindgen-csharp 0.30.0 exactly for every
    /// primitive, including char's asymmetric plain-cast lower
    /// vs. unchecked lift.
    /// </summary>
    internal static class PrimMarshal
    {
        /// <summary>
        /// Core-wasm ABI type for a primitive — int for 8/16/32-bit
        /// ints + bool + char, long for 64-bit ints, f32/f64 as-is.
        /// </summary>
        public static string StubType(CtPrim kind) => kind switch
        {
            CtPrim.F32 => "float",
            CtPrim.F64 => "double",
            CtPrim.S64 => "long",
            CtPrim.U64 => "long",
            _ => "int",
        };

        /// <summary>
        /// C# wrapper-type for a primitive (the user-visible shape).
        /// Same as <see cref="TypeRefEmit.EmitPrim"/>; restated here
        /// to keep lift/lower logic self-contained.
        /// </summary>
        public static string WrapType(CtPrim kind) =>
            TypeRefEmit.EmitPrim(kind);

        /// <summary>
        /// Expression converting a wrapper-side value to its
        /// core-wasm ABI form. Used for import-wrapper args (→ stub)
        /// and export-trampoline returns (wrapper ret → stub ret).
        /// </summary>
        public static string Lower(CtPrim kind, string expr) => kind switch
        {
            CtPrim.Bool => $"({expr} ? 1 : 0)",
            CtPrim.S8 or CtPrim.U8 or CtPrim.S16 or CtPrim.U16
                or CtPrim.S32 or CtPrim.S64 or CtPrim.F32
                or CtPrim.F64 => expr,
            CtPrim.U32 => $"unchecked((int)({expr}))",
            CtPrim.U64 => $"unchecked((long)({expr}))",
            // char asymmetry: plain cast on lower.
            CtPrim.Char => $"((int){expr})",
            _ => throw new NotImplementedException(
                "Lower for " + kind + " is a follow-up."),
        };

        /// <summary>
        /// Expression converting a stub-side return to the
        /// wrapper-type. Used for import-wrapper returns (stub →
        /// wrapper ret) and export-trampoline args (stub param →
        /// wrapper arg).
        /// </summary>
        public static string Lift(CtPrim kind, string expr) => kind switch
        {
            CtPrim.Bool => $"({expr} != 0)",
            CtPrim.S8 => $"((sbyte){expr})",
            CtPrim.U8 => $"((byte){expr})",
            CtPrim.S16 => $"((short){expr})",
            CtPrim.U16 => $"((ushort){expr})",
            CtPrim.S32 or CtPrim.S64 or CtPrim.F32 or CtPrim.F64 => expr,
            CtPrim.U32 => $"unchecked((uint)({expr}))",
            CtPrim.U64 => $"unchecked((ulong)({expr}))",
            // char asymmetry: unchecked cast on lift.
            CtPrim.Char => $"unchecked((uint)({expr}))",
            _ => throw new NotImplementedException(
                "Lift for " + kind + " is a follow-up."),
        };
    }
}
