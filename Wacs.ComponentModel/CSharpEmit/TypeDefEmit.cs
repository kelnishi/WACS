// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Emits wit-bindgen-csharp-shaped nested type declarations
    /// inside an interface file. Each WIT named type (<c>record</c>,
    /// <c>variant</c>, <c>enum</c>, <c>flags</c>, <c>resource</c>,
    /// plus type aliases) becomes a C# class/struct/enum nested
    /// inside the <c>public interface I{Name} { … }</c>.
    ///
    /// <para><b>Phase 1a.2 scope:</b> <c>enum</c> and <c>flags</c>.
    /// <c>record</c> / <c>variant</c> / <c>resource</c> / type
    /// aliases are follow-up commits — each has its own shape and
    /// deserves its own emitter + snapshot test.</para>
    /// </summary>
    internal static class TypeDefEmit
    {
        /// <summary>
        /// Emit a <see cref="CtNamedType"/> as a nested type inside
        /// the interface (4-space indented from the interface body).
        /// Returns the source text with a trailing newline after the
        /// closing brace. Callers control inter-type spacing.
        /// </summary>
        public static string Emit(CtNamedType named)
        {
            return named.Type switch
            {
                CtEnumType e => EmitEnum(named.Name, e),
                CtFlagsType f => EmitFlags(named.Name, f),
                _ => throw new NotImplementedException(
                    "Type emission for " + named.Type.GetType().Name +
                    " is a Phase 1a.2 follow-up."),
            };
        }

        // ---- enum ----------------------------------------------------------

        /// <summary>
        /// Plain C# enum, no backing type (defaults to int).
        /// wit-bindgen-csharp places all cases on a single line,
        /// comma-separated, inside braces on separate lines:
        /// <code>
        ///     public enum Color {
        ///         RED, GREEN, BLUE
        ///     }
        /// </code>
        /// Case names are UPPER_SNAKE_CASE of the WIT names.
        /// </summary>
        private static string EmitEnum(string name, CtEnumType e)
        {
            var sb = new StringBuilder();
            sb.Append("    public enum ");
            sb.Append(NameConventions.ToPascalCase(name));
            sb.Append(" {\n");
            sb.Append("        ");
            for (int i = 0; i < e.Cases.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(NameConventions.ToUpperSnake(e.Cases[i]));
            }
            sb.Append('\n');
            sb.Append("    }\n");
            return sb.ToString();
        }

        // ---- flags ---------------------------------------------------------

        /// <summary>
        /// Bit-flag enum with explicit <c>1 &lt;&lt; i</c> values and
        /// a backing type sized to the flag count: byte (≤8),
        /// ushort (≤16), uint (≤32), ulong (≤64). Note: NO
        /// <c>[Flags]</c> attribute — wit-bindgen-csharp 0.30.0
        /// doesn't emit one.
        /// </summary>
        private static string EmitFlags(string name, CtFlagsType f)
        {
            var sb = new StringBuilder();
            sb.Append("    public enum ");
            sb.Append(NameConventions.ToPascalCase(name));
            sb.Append(" : ");
            sb.Append(FlagsBackingType(f.Flags.Count));
            sb.Append(" {\n");
            for (int i = 0; i < f.Flags.Count; i++)
            {
                sb.Append("        ");
                sb.Append(NameConventions.ToUpperSnake(f.Flags[i]));
                sb.Append(" = 1 << ");
                sb.Append(i);
                sb.Append(",\n");
            }
            sb.Append("    }\n");
            return sb.ToString();
        }

        private static string FlagsBackingType(int flagCount)
        {
            if (flagCount <= 8) return "byte";
            if (flagCount <= 16) return "ushort";
            if (flagCount <= 32) return "uint";
            if (flagCount <= 64) return "ulong";
            throw new ArgumentOutOfRangeException(
                nameof(flagCount),
                "Flags type with more than 64 members is not supported " +
                "(component-model spec limit).");
        }
    }
}
