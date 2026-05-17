// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Text;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// Naming helpers specific to harness emission. The transpiler-side
    /// <see cref="NameMangler"/> uses dotted multi-segment namespaces
    /// (<c>World.wit.exports.wasi.cli.run.v0_2_0</c>); the harness
    /// collapses each interface into a single PascalCase segment
    /// (<c>WasiCliRun</c>) for cleaner type / method names.
    /// </summary>
    internal static class HarnessNaming
    {
        /// <summary>
        /// PascalCase segment for an interface — concatenates the
        /// package namespace + path + interface name. Version is
        /// omitted in v1 (two versions of the same interface in one
        /// world is rare; revisit if it surfaces).
        ///
        /// <para><c>wasi:cli/run@0.2.0</c> → <c>WasiCliRun</c>.</para>
        /// </summary>
        public static string InterfaceSegment(CtInterfaceType iface)
        {
            var sb = new StringBuilder();
            if (iface.Package != null)
            {
                sb.Append(NameMangler.ToPascalCase(iface.Package.Namespace));
                foreach (var seg in iface.Package.Path)
                    sb.Append(NameMangler.ToPascalCase(seg));
            }
            sb.Append(NameMangler.ToPascalCase(iface.Name));
            return sb.ToString();
        }

        /// <summary>
        /// PascalCase C# method name for an interface-export function
        /// emitted flat on the harness — <c>WasiCliRun_Run</c>.
        /// </summary>
        public static string InterfaceFunctionPascal(CtInterfaceType iface, string fnKebab) =>
            InterfaceSegment(iface) + "_" + NameMangler.ToPascalCase(fnKebab);

        /// <summary>
        /// C#-safe field-name slug for an interface-export function —
        /// <c>wasi_cli_run_run</c>. Used to build <c>_invoke_*</c> /
        /// <c>_post_*</c> / <c>Lift__ret_*</c> identifier suffixes.
        /// </summary>
        public static string InterfaceFunctionSlug(CtInterfaceType iface, string fnKebab)
        {
            var sb = new StringBuilder();
            if (iface.Package != null)
            {
                sb.Append(SanitizeForSlug(iface.Package.Namespace));
                foreach (var seg in iface.Package.Path)
                {
                    sb.Append('_');
                    sb.Append(SanitizeForSlug(seg));
                }
            }
            sb.Append('_');
            sb.Append(SanitizeForSlug(iface.Name));
            sb.Append('_');
            sb.Append(SanitizeForSlug(fnKebab));
            return sb.ToString();
        }

        private static string SanitizeForSlug(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '-' || c == '.' || c == '/' || c == ':' || c == '@' || c == '#')
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
