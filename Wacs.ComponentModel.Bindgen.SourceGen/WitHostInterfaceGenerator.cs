// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.ComponentModel.Bindgen.SourceGen
{
    /// <summary>
    /// Roslyn incremental source generator: parses WIT files
    /// listed as <c>&lt;AdditionalFiles&gt;</c> with metadata
    /// <c>WitForHost=true</c>, emits one
    /// <c>public interface IXxx</c> file per WIT interface, and
    /// pipes them into the consuming project's compilation.
    ///
    /// <para>To wire the generator into a project:</para>
    /// <code>
    /// &lt;ItemGroup&gt;
    ///   &lt;ProjectReference Include="..\Wacs.ComponentModel.Bindgen.SourceGen\..."
    ///                     OutputItemType="Analyzer"
    ///                     ReferenceOutputAssembly="false" /&gt;
    ///   &lt;AdditionalFiles Include="wit\**\*.wit" WitForHost="true" /&gt;
    /// &lt;/ItemGroup&gt;
    /// </code>
    ///
    /// <para>The generated code references <c>Result&lt;,&gt;</c>,
    /// <c>Option&lt;&gt;</c>, <c>Unit</c>, and
    /// <c>WitSourceAttribute</c> from
    /// <c>Wacs.ComponentModel.Runtime</c> — the consuming project
    /// must reference <c>Wacs.ComponentModel</c>.</para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class WitHostInterfaceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Pull every AdditionalFile flagged WitForHost=true.
            // Each surfaces as a (path, content) pair the gen can
            // funnel through WitLoader.
            var witFiles = context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Where(static pair =>
                {
                    var (file, opts) = pair;
                    if (!file.Path.EndsWith(".wit",
                            System.StringComparison.OrdinalIgnoreCase))
                        return false;
                    var fileOpts = opts.GetOptions(file);
                    return fileOpts.TryGetValue(
                        "build_metadata.AdditionalFiles.WitForHost",
                        out var v)
                        && string.Equals(v, "true",
                            System.StringComparison.OrdinalIgnoreCase);
                })
                .Select(static (pair, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var (file, _) = pair;
                    return (Path: file.Path,
                            Text: file.GetText(ct)?.ToString() ?? "");
                })
                .Collect();

            // Optional: project-level metadata to override the
            // default namespace.
            var nsOverride = context.AnalyzerConfigOptionsProvider
                .Select(static (opts, _) =>
                {
                    opts.GlobalOptions.TryGetValue(
                        "build_property.WitHostNamespaceOverride",
                        out var v);
                    return v;
                });

            var combined = witFiles.Combine(nsOverride);

            context.RegisterSourceOutput(combined,
                (spc, pair) => Execute(spc, pair.Left, pair.Right));
        }

        private static void Execute(SourceProductionContext context,
            System.Collections.Immutable.ImmutableArray<(string Path, string Text)> files,
            string? nsOverride)
        {
            if (files.IsDefaultOrEmpty) return;

            // Parse each AdditionalFile's text in-memory and merge
            // documents into packages — no disk I/O, satisfying
            // RS1035 (analyzers must be deterministic + sandboxed).
            // WitLoader.MergeDocuments handles the package-name
            // attribution + cross-file resolution; the Path
            // metadata only matters for diagnostics, not for
            // resolution itself.
            // Group files by their containing directory. Each
            // directory is one WIT package context — headerless
            // files attribute to the package declared by their
            // sibling. Mirrors WitLoader.LoadDirectoryTree but
            // works from in-memory paths so we don't touch disk.
            var byDir = new Dictionary<string, List<(string Path, string Text)>>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var dir = GetDirectoryName(f.Path);
                if (!byDir.TryGetValue(dir, out var list))
                {
                    list = new List<(string, string)>();
                    byDir[dir] = list;
                }
                list.Add(f);
            }

            IReadOnlyList<CtPackage> packages;
            try
            {
                var allPkgs = new List<CtPackage>();
                foreach (var group in byDir.Values)
                {
                    var docs = new List<WitDocument>(group.Count);
                    foreach (var f in group)
                        docs.Add(WitParser.Parse(f.Text));
                    allPkgs.AddRange(WitLoader.MergeDocuments(docs));
                }
                packages = WitLoader_MergeByQualifiedName(allPkgs);
                // Resolve cross-package type refs (filling
                // CtTypeRef.Target for `use` imports across
                // packages — required for emit's cross-interface
                // qualification path).
                WitResolver.Resolve(packages);
            }
            catch (System.Exception ex)
            {
                ReportError(context, "WACS001",
                    "Failed to parse WIT input",
                    "WIT parsing threw: " + ex.Message);
                return;
            }

            var options = new EmitOptions
            {
                HostInterfaceMode = true,
                HostNamespaceOverride =
                    string.IsNullOrEmpty(nsOverride) ? null : nsOverride,
            };

            foreach (var pkg in packages)
            {
                IReadOnlyList<EmittedSource> sources;
                try
                {
                    sources = HostInterfaceEmit.EmitPackage(
                        pkg, packages, options);
                }
                catch (System.Exception ex)
                {
                    ReportError(context, "WACS002",
                        "Host-interface emission failed",
                        "Package " + pkg.Name + ": " + ex.Message);
                    continue;
                }
                foreach (var s in sources)
                {
                    context.AddSource(s.FileName,
                        SourceText.From(s.Content, Encoding.UTF8));
                }
            }
        }

        // System.IO.Path is partially banned by RS1035; this is
        // a pure-string rfind over the path separator.
        private static string GetDirectoryName(string path)
        {
            int last = -1;
            for (int i = path.Length - 1; i >= 0; i--)
            {
                if (path[i] == '/' || path[i] == '\\')
                {
                    last = i;
                    break;
                }
            }
            return last < 0 ? "" : path.Substring(0, last);
        }

        // Replicates WitLoader.MergeByQualifiedName's logic
        // without disk-IO dependencies. Coalesces same-named
        // packages by concatenating their interfaces + worlds.
        private static List<CtPackage> WitLoader_MergeByQualifiedName(
            IReadOnlyList<CtPackage> packages)
        {
            var byKey = new Dictionary<string, (CtPackageName Name,
                List<CtInterfaceType> Ifaces, List<CtWorldType> Worlds)>();
            foreach (var pkg in packages)
            {
                var key = pkg.Name.ToString();
                if (!byKey.TryGetValue(key, out var acc))
                {
                    acc = (pkg.Name,
                        new List<CtInterfaceType>(),
                        new List<CtWorldType>());
                    byKey[key] = acc;
                }
                acc.Ifaces.AddRange(pkg.Interfaces);
                acc.Worlds.AddRange(pkg.Worlds);
                byKey[key] = acc;
            }
            var result = new List<CtPackage>();
            foreach (var kv in byKey.Values)
                result.Add(new CtPackage(kv.Name, kv.Ifaces, kv.Worlds));
            return result;
        }

        private static void ReportError(
            SourceProductionContext ctx, string id, string title,
            string message)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id, title, message, "WIT",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                Location.None));
        }
    }
}
