// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Shared resolver for the two <c>--harness</c> / <c>--wit-dir</c>
    /// surfaces used by <c>wacs build</c>, <c>wacs aot</c>, and
    /// <c>wacs run</c>.
    ///
    /// <para><c>--harness &lt;dll&gt;</c> loads a `wacs harness`-produced
    /// assembly and reads the <c>_WitContract</c> static field that
    /// `HarnessEmitter` embedded at build time.</para>
    ///
    /// <para><c>--wit-dir &lt;dir&gt;</c> concatenates every <c>.wit</c>
    /// file under the directory tree — the same shape `HarnessEmitter`
    /// uses internally when embedding the contract.</para>
    ///
    /// <para>The flags are mutually exclusive. Returns <c>null</c> when
    /// neither is set; throws on conflicts / read errors so callers
    /// surface clear messages early.</para>
    /// </summary>
    internal static class HarnessContractLoader
    {
        /// <summary>
        /// Resolve the harness contract WIT text from the supplied
        /// flag values. <paramref name="verbLabel"/> appears in error
        /// messages so the user sees the originating verb ("wacs aot",
        /// "wacs run", …).
        /// </summary>
        public static string? Resolve(
            string? harness, string? witDir, string verbLabel)
        {
            bool haveHarness = !string.IsNullOrEmpty(harness);
            bool haveWitDir = !string.IsNullOrEmpty(witDir);
            if (!haveHarness && !haveWitDir) return null;
            if (haveHarness && haveWitDir)
                throw new ArgumentException(
                    verbLabel + ": --harness and --wit-dir are mutually exclusive.");

            if (haveHarness)
            {
                var path = Path.GetFullPath(harness!);
                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        verbLabel + ": --harness file not found: " + path);
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                foreach (var t in asm.GetTypes())
                {
                    var field = t.GetField("_WitContract",
                        BindingFlags.Public | BindingFlags.Static);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        var s = (string?)field.GetValue(null);
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
                throw new InvalidOperationException(
                    verbLabel + ": --harness '" + path
                    + "' carries no _WitContract field. "
                    + "Was it produced by `wacs harness`?");
            }

            var dir = Path.GetFullPath(witDir!);
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException(
                    verbLabel + ": --wit-dir directory not found: " + dir);
            var sb = new StringBuilder();
            foreach (var p in Directory.EnumerateFiles(dir, "*.wit",
                SearchOption.AllDirectories))
            {
                sb.AppendLine("// === " + Path.GetRelativePath(dir, p) + " ===");
                sb.Append(File.ReadAllText(p));
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
