// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Thin facade over <see cref="NameMangler"/> for the pure
    /// kebab-case conversions and namespace assembly. The canonical
    /// implementations live in <see cref="NameMangler"/> (which also
    /// carries C# keyword escaping and a collision-resolution
    /// <c>NameScope</c>); this type is retained for call-site
    /// readability — <c>NameConventions.ToPascalCase</c> makes
    /// intent clearer than <c>NameMangler.ToPascalCase</c> at the
    /// type-level emitters.
    /// </summary>
    internal static class NameConventions
    {
        public static string ToPascalCase(string kebab) =>
            NameMangler.ToPascalCase(kebab);

        public static string ToCamelCase(string kebab) =>
            NameMangler.ToCamelCase(kebab);

        public static string ToUpperSnake(string kebab) =>
            NameMangler.ToUpperSnake(kebab);

        public static string SanitizeVersion(string? version) =>
            NameMangler.SanitizeVersion(version);

        public static string InterfaceNamespace(string worldName, bool isExport,
                                                CtPackageName pkg) =>
            NameMangler.InterfaceNamespace(worldName, isExport, pkg);

        public static string WorldNamespaceName(string worldNameKebab) =>
            NameMangler.WorldNamespaceName(worldNameKebab);

        public static string WorldFileBaseName(string worldNameKebab) =>
            NameMangler.WorldFileBaseName(worldNameKebab);
    }
}
