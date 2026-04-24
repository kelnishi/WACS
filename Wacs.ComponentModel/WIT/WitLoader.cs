// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.WIT
{
    /// <summary>
    /// Directory-based loader for multi-file WIT packages. A
    /// single WIT package can span several <c>.wit</c> files: the
    /// package header (<c>package foo:bar@ver;</c>) appears in at
    /// least one file, and sibling files without a header are
    /// implicitly part of the same package. Files in subdirectories
    /// belong to their own packages.
    ///
    /// <para>This is the entry point for loading real-world WIT
    /// trees like <c>wasi-cli/wit/</c> — just parsing each file
    /// individually leaves headerless files in an anonymous
    /// package, and produces separate duplicates of the named
    /// package for files that do declare it. <see cref="LoadDirectory"/>
    /// merges everything.</para>
    /// </summary>
    public static class WitLoader
    {
        /// <summary>
        /// Load every <c>.wit</c> file directly under
        /// <paramref name="directory"/> (non-recursive) and return
        /// a merged list of <see cref="CtPackage"/>s. Headerless
        /// files are attributed to whichever package the
        /// header-bearing files in the same directory declare;
        /// duplicate packages across files (same namespace / path
        /// / version) are coalesced — their interfaces and worlds
        /// concatenate into a single <c>CtPackage</c>.
        /// </summary>
        public static List<CtPackage> LoadDirectory(string directory)
        {
            var files = Directory.GetFiles(directory, "*.wit");
            return LoadFiles(files);
        }

        /// <summary>
        /// Same as <see cref="LoadDirectory"/> but recurses into
        /// subdirectories — each subdirectory independently
        /// contributes its own packages. Useful for loading a WIT
        /// tree with <c>deps/</c> subpackages
        /// (<c>wasi-cli/wit/deps/io/</c> etc.).
        /// </summary>
        public static List<CtPackage> LoadDirectoryTree(string directory)
        {
            var merged = new List<CtPackage>();
            merged.AddRange(LoadDirectory(directory));
            foreach (var sub in Directory.GetDirectories(directory))
                merged.AddRange(LoadDirectoryTree(sub));
            return MergeByQualifiedName(merged);
        }

        /// <summary>
        /// Parse and merge a specific list of <c>.wit</c> file
        /// paths. Exposed for callers that want to control the
        /// file set explicitly (e.g. tests exercising a curated
        /// subset). All files are treated as members of the same
        /// WIT package space: headerless files are attributed to
        /// the named package declared in the set.
        /// </summary>
        public static List<CtPackage> LoadFiles(IEnumerable<string> paths)
        {
            var docs = new List<WitDocument>();
            foreach (var path in paths)
            {
                var src = File.ReadAllText(path);
                docs.Add(WitParser.Parse(src));
            }
            return MergeDocuments(docs);
        }

        /// <summary>
        /// Merge already-parsed <see cref="WitDocument"/>s into
        /// <see cref="CtPackage"/>s. Headerless documents (packages
        /// with empty namespace and no path) are attributed to the
        /// first named package encountered; same-named packages
        /// coalesce by concatenating interfaces + worlds.
        /// </summary>
        public static List<CtPackage> MergeDocuments(IEnumerable<WitDocument> docs)
        {
            // Pass 1: find a dominant named package across all docs
            // (typically there's exactly one for a single directory).
            // Anonymous / headerless packages get attributed to it.
            WitPackageName? dominant = null;
            foreach (var doc in docs)
            {
                foreach (var p in doc.Packages)
                {
                    if (IsNamed(p.Name))
                    {
                        if (dominant == null) dominant = p.Name;
                        // Later declarations of a same-named package
                        // don't change the dominant; they'll merge.
                        break;
                    }
                }
                if (dominant != null) break;
            }

            // Pass 2: clone each WitPackage, attributing anonymous
            // ones to the dominant name, then convert.
            var stamped = new List<WitDocument>();
            foreach (var doc in docs)
            {
                foreach (var p in doc.Packages)
                {
                    if (!IsNamed(p.Name) && dominant != null)
                        p.Name = dominant;
                }
                stamped.Add(doc);
            }

            // Pass 3: convert each doc separately (each producing
            // one or more CtPackages), then coalesce by qualified
            // name.
            var converted = new List<CtPackage>();
            foreach (var doc in stamped)
                converted.AddRange(WitToTypes.Convert(doc));

            return MergeByQualifiedName(converted);
        }

        private static bool IsNamed(WitPackageName? n) =>
            n != null && (!string.IsNullOrEmpty(n.Namespace) || n.Path.Count > 0);

        private static List<CtPackage> MergeByQualifiedName(
            IEnumerable<CtPackage> packages)
        {
            // Key by the package's string form — includes namespace,
            // path, and version. Packages that share the key merge
            // their interface and world lists.
            var byKey = new Dictionary<string, (CtPackageName name,
                                                List<CtInterfaceType> ifaces,
                                                List<CtWorldType> worlds)>();
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
                acc.ifaces.AddRange(pkg.Interfaces);
                acc.worlds.AddRange(pkg.Worlds);
                byKey[key] = acc;
            }
            var result = new List<CtPackage>(byKey.Count);
            foreach (var kv in byKey)
                result.Add(new CtPackage(kv.Value.name, kv.Value.ifaces, kv.Value.worlds));
            return result;
        }
    }
}
