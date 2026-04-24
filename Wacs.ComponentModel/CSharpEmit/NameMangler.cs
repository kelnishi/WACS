// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Generic;
using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Kebab-case ↔ C# identifier conversions + C# keyword escaping.
    /// Produces identifiers that are guaranteed compilable (never a
    /// C# keyword, no leading digit).
    ///
    /// <para>All transformations are pure functions of their input
    /// kebab name. Collision resolution within a scope (e.g., two
    /// variant cases whose PascalCase forms coincide) is handled by
    /// <see cref="NameScope"/>.</para>
    ///
    /// <para>Cases-of-interest for each transform are covered by unit
    /// tests in <c>Wacs.ComponentModel.Test/NameManglerTests.cs</c>.</para>
    /// </summary>
    internal static class NameMangler
    {
        // ---- Case conversions --------------------------------------------

        /// <summary>
        /// kebab-case → PascalCase.
        /// <c>input-stream</c> → <c>InputStream</c>.
        /// Empty string returns empty string.
        /// </summary>
        public static string ToPascalCase(string kebab)
        {
            if (string.IsNullOrEmpty(kebab)) return kebab;
            var sb = new StringBuilder(kebab.Length);
            bool upperNext = true;
            foreach (var c in kebab)
            {
                if (c == '-' || c == '_') { upperNext = true; continue; }
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// kebab-case → camelCase.
        /// <c>last-operation-failed</c> → <c>lastOperationFailed</c>.
        /// </summary>
        public static string ToCamelCase(string kebab)
        {
            var pascal = ToPascalCase(kebab);
            if (string.IsNullOrEmpty(pascal)) return pascal;
            return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        /// <summary>
        /// kebab-case → UPPER_SNAKE_CASE.
        /// <c>last-operation-failed</c> → <c>LAST_OPERATION_FAILED</c>.
        /// </summary>
        public static string ToUpperSnake(string kebab)
        {
            if (string.IsNullOrEmpty(kebab)) return kebab;
            return kebab.Replace('-', '_').ToUpperInvariant();
        }

        // ---- Version sanitization ----------------------------------------

        /// <summary>
        /// WIT semver → wit-bindgen-csharp namespace form.
        /// <c>0.2.3</c> → <c>v0_2_3</c>; null/empty → empty string.
        /// </summary>
        public static string SanitizeVersion(string? version)
        {
            if (string.IsNullOrEmpty(version)) return string.Empty;
            return "v" + version.Replace('.', '_').Replace('-', '_').Replace('+', '_');
        }

        // ---- C# keyword escape -------------------------------------------

        /// <summary>
        /// Every C# reserved keyword — mangling targets must not
        /// overlap. Source: the C# language reference; this list
        /// matches up through C# 12 (includes contextual keywords
        /// that break in declaration sites such as <c>record</c>).
        /// </summary>
        private static readonly HashSet<string> CSharpReservedKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "abstract", "as", "base", "bool", "break", "byte", "case",
                "catch", "char", "checked", "class", "const", "continue",
                "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally",
                "fixed", "float", "for", "foreach", "goto", "if", "implicit",
                "in", "int", "interface", "internal", "is", "lock", "long",
                "namespace", "new", "null", "object", "operator", "out",
                "override", "params", "private", "protected", "public",
                "readonly", "ref", "return", "sbyte", "sealed", "short",
                "sizeof", "stackalloc", "static", "string", "struct",
                "switch", "this", "throw", "true", "try", "typeof", "uint",
                "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
                "void", "volatile", "while",
            };

        /// <summary>
        /// If <paramref name="name"/> collides with a C# reserved
        /// keyword, prefix it with <c>@</c> so it's a valid
        /// identifier. Otherwise return as-is.
        /// </summary>
        public static string EscapeIfKeyword(string name)
        {
            return CSharpReservedKeywords.Contains(name) ? "@" + name : name;
        }

        /// <summary>
        /// True when <paramref name="name"/> would be a C# reserved
        /// keyword requiring <c>@</c> escape.
        /// </summary>
        public static bool IsCSharpKeyword(string name) =>
            CSharpReservedKeywords.Contains(name);

        // ---- Namespace / path assembly -----------------------------------

        /// <summary>
        /// <c>{World}.wit.{imports|exports}.{ns}.{path}.{ver?}</c>
        /// — the per-interface file namespace.
        /// </summary>
        public static string InterfaceNamespace(string worldName, bool isExport,
                                                CtPackageName pkg)
        {
            var sb = new StringBuilder();
            sb.Append(WorldNamespaceName(worldName));
            sb.Append('.');
            sb.Append("wit");
            sb.Append('.');
            sb.Append(isExport ? "exports" : "imports");
            sb.Append('.');
            sb.Append(pkg.Namespace);
            foreach (var seg in pkg.Path)
            {
                sb.Append('.');
                sb.Append(seg);
            }
            var v = SanitizeVersion(pkg.Version);
            if (v.Length > 0)
            {
                sb.Append('.');
                sb.Append(v);
            }
            return sb.ToString();
        }

        /// <summary>
        /// <c>{ns}.{path}.{v?}</c> — the dotted form of a package
        /// used in emitted file names.
        /// </summary>
        public static string JoinPackagePath(CtPackageName pkg)
        {
            var sb = new StringBuilder();
            sb.Append(pkg.Namespace);
            foreach (var seg in pkg.Path)
            {
                sb.Append('.');
                sb.Append(seg);
            }
            var v = SanitizeVersion(pkg.Version);
            if (v.Length > 0)
            {
                sb.Append('.');
                sb.Append(v);
            }
            return sb.ToString();
        }

        /// <summary>
        /// World namespace name: PascalCase of kebab world name +
        /// <c>World</c> suffix (always appended, even when the
        /// kebab name already ends in <c>world</c>).
        /// <c>hello</c> → <c>HelloWorld</c>;
        /// <c>prim-world</c> → <c>PrimWorldWorld</c>.
        /// </summary>
        public static string WorldNamespaceName(string worldNameKebab) =>
            ToPascalCase(worldNameKebab) + "World";

        /// <summary>
        /// World file base name: PascalCase with NO <c>World</c>
        /// suffix. <c>hello</c> → <c>Hello</c>;
        /// <c>command</c> → <c>Command</c>.
        /// </summary>
        public static string WorldFileBaseName(string worldNameKebab) =>
            ToPascalCase(worldNameKebab);
    }

    /// <summary>
    /// Tracks C# identifier names already used within a scope
    /// (variant cases, record fields, function params, etc.) so
    /// the emitter can produce a distinct name when a kebab →
    /// PascalCase transformation collides with a previous one.
    /// Collisions resolve by appending <c>N</c> starting at 1.
    ///
    /// <para>Not used in practice for wit-bindgen-compat output
    /// (wit-bindgen's naming rules preclude collisions in every
    /// fixture we've verified). Retained as an explicit
    /// collision-resolution handle for future aggregate emitters
    /// and for user-supplied impls that may introduce duplicate
    /// names inadvertently.</para>
    /// </summary>
    internal sealed class NameScope
    {
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Request <paramref name="proposed"/> as a C# identifier
        /// within this scope. If it's already taken, return
        /// <c>{proposed}1</c>, <c>{proposed}2</c>, … until a free
        /// name is found. Records the allocated name.
        /// </summary>
        public string Claim(string proposed)
        {
            if (_used.Add(proposed)) return proposed;
            for (int i = 1; ; i++)
            {
                var candidate = proposed + i;
                if (_used.Add(candidate)) return candidate;
            }
        }

        /// <summary>True iff <paramref name="name"/> has been claimed.</summary>
        public bool IsClaimed(string name) => _used.Contains(name);
    }
}
