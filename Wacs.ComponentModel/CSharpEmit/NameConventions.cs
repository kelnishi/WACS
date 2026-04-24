// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Naming conventions that translate WIT / component-model names
    /// into wit-bindgen-csharp's C# output shape. Every rule here
    /// mirrors a decision the upstream tool made; we match them to
    /// preserve the roundtrip invariant.
    /// </summary>
    internal static class NameConventions
    {
        /// <summary>
        /// Convert a kebab-case WIT name to PascalCase.
        /// <c>input-stream</c> → <c>InputStream</c>.
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
        /// Convert a kebab-case WIT name to camelCase.
        /// <c>last-operation-failed</c> → <c>lastOperationFailed</c>.
        /// </summary>
        public static string ToCamelCase(string kebab)
        {
            var pascal = ToPascalCase(kebab);
            if (string.IsNullOrEmpty(pascal)) return pascal;
            return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        /// <summary>
        /// Convert a kebab-case WIT name to UPPER_SNAKE_CASE.
        /// <c>last-operation-failed</c> → <c>LAST_OPERATION_FAILED</c>.
        /// Used for the public <c>const byte</c> discriminants
        /// wit-bindgen-csharp emits inside variant classes.
        /// </summary>
        public static string ToUpperSnake(string kebab)
        {
            if (string.IsNullOrEmpty(kebab)) return kebab;
            return kebab.Replace('-', '_').ToUpperInvariant();
        }

        /// <summary>
        /// Sanitize a WIT version string into the form wit-bindgen-csharp
        /// uses in namespace paths. <c>0.2.3</c> → <c>v0_2_3</c>.
        /// Null/empty returns an empty string so callers can concatenate
        /// unconditionally.
        /// </summary>
        public static string SanitizeVersion(string? version)
        {
            if (string.IsNullOrEmpty(version)) return string.Empty;
            return "v" + version.Replace('.', '_').Replace('-', '_').Replace('+', '_');
        }

        /// <summary>
        /// Assemble the namespace a per-interface file lives in:
        /// <c>{World}.wit.{imports|exports}.{pkg-ns}.{pkg-path}.{version}</c>.
        /// For <c>wasi:io/streams@0.2.3</c> imported into world
        /// <c>hello</c>, produces <c>HelloWorld.wit.imports.wasi.io.v0_2_3</c>.
        /// </summary>
        public static string InterfaceNamespace(string worldName,
                                                bool isExport,
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
        /// The namespace name for the world itself. wit-bindgen-csharp
        /// takes the PascalCase world name and <b>always</b> appends
        /// <c>World</c> — even if the kebab name already ends in
        /// <c>world</c>.
        /// <c>hello</c> → <c>HelloWorld</c>;
        /// <c>command</c> → <c>CommandWorld</c>;
        /// <c>prim-world</c> → <c>PrimWorldWorld</c> (confirmed against
        /// wit-bindgen-csharp 0.30.0 output).
        /// </summary>
        public static string WorldNamespaceName(string worldNameKebab)
        {
            return ToPascalCase(worldNameKebab) + "World";
        }

        /// <summary>
        /// The top-level world-file name (without .cs extension).
        /// wit-bindgen-csharp uses the PascalCase world name with NO
        /// "World" suffix: <c>hello</c> → <c>Hello</c>, <c>command</c>
        /// → <c>Command</c>.
        /// </summary>
        public static string WorldFileBaseName(string worldNameKebab) =>
            ToPascalCase(worldNameKebab);
    }
}
