// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;

namespace Wacs.HostBindings
{
    /// <summary>
    /// Assembly-level table of <c>(IImports method name, wasm module,
    /// wasm name)</c> tuples emitted by the transpiler so the source
    /// generator can match each IImports method to a <c>[WacsImport]</c>
    /// binding without parsing the sanitized C# method name.
    ///
    /// <para>The <see cref="Mapping"/> string is a semicolon-separated
    /// list of three-field records, each colon-separated:
    /// <c>methodName:wasmModule:wasmName</c>. Module names contain
    /// arbitrary characters (<c>wasi:io/streams@0.2.3</c>) so semicolons
    /// and colons inside fields are escaped as <c>\;</c> and <c>\:</c>.
    /// Multiple <c>[WacsImportNames]</c> attributes per assembly are
    /// supported (one per IImports interface) — the source gen unions
    /// them.</para>
    ///
    /// <para>Why string-encoded: <c>Lokad.ILPack</c> — our dynamic-
    /// assembly → PE serializer — preserves assembly-level custom
    /// attributes but drops method-level ones, so the obvious shape
    /// (a <c>[WacsImportName]</c> on each IImports method) doesn't
    /// survive to the saved .dll. An assembly-level table workarounds
    /// the limitation cleanly.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WacsImportNamesAttribute : Attribute
    {
        public string Mapping { get; }

        public WacsImportNamesAttribute(string mapping)
        {
            Mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        }

        // Encode/Decode the string format. Field separator is colon,
        // record separator is semicolon. Backslash escapes either.

        public static string Encode(System.Collections.Generic.IEnumerable<(string MethodName, string Module, string Name)> entries)
        {
            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (var (methodName, mod, name) in entries)
            {
                if (!first) sb.Append(';');
                first = false;
                AppendEscaped(sb, methodName);
                sb.Append(':');
                AppendEscaped(sb, mod);
                sb.Append(':');
                AppendEscaped(sb, name);
            }
            return sb.ToString();
        }

        public static System.Collections.Generic.List<(string MethodName, string Module, string Name)> Decode(string mapping)
        {
            var result = new System.Collections.Generic.List<(string, string, string)>();
            if (string.IsNullOrEmpty(mapping)) return result;
            int i = 0;
            while (i < mapping.Length)
            {
                var fields = new string[3];
                for (int f = 0; f < 3; f++)
                {
                    var sb = new System.Text.StringBuilder();
                    while (i < mapping.Length)
                    {
                        char c = mapping[i];
                        if (c == '\\' && i + 1 < mapping.Length)
                        {
                            sb.Append(mapping[i + 1]);
                            i += 2;
                            continue;
                        }
                        if (c == ':' || c == ';') break;
                        sb.Append(c);
                        i++;
                    }
                    fields[f] = sb.ToString();
                    if (f < 2 && i < mapping.Length && mapping[i] == ':') i++;
                }
                if (i < mapping.Length && mapping[i] == ';') i++;
                result.Add((fields[0], fields[1], fields[2]));
            }
            return result;
        }

        private static void AppendEscaped(System.Text.StringBuilder sb, string s)
        {
            foreach (var c in s)
            {
                if (c == '\\' || c == ':' || c == ';') sb.Append('\\');
                sb.Append(c);
            }
        }
    }
}
