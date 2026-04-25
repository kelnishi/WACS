// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.ComponentModel.Bindgen
{
    /// <summary>
    /// Forward direction (WIT text → C# bindings). Thin wrapper
    /// over <see cref="CSharpEmitter"/> + <see cref="WitParser"/>
    /// /<see cref="WitLoader"/> — its job is plumbing, not
    /// emission logic. Source generators, build-time MSBuild
    /// targets, and the <c>wit-bindgen-wacs</c> CLI all funnel
    /// through one of the entry points here so the emission shape
    /// stays consistent across delivery channels.
    /// </summary>
    public static class WitForward
    {
        /// <summary>
        /// Parse a single WIT source string and emit one
        /// <see cref="EmittedSource"/> per generated file. The
        /// caller decides what to do with them — write to disk,
        /// pipe into a Roslyn source-gen <c>SourceProductionContext</c>,
        /// pin as snapshot fixtures.
        /// </summary>
        /// <param name="witSource">WIT IDL text. Must contain a
        /// world declaration; the first world emits.</param>
        /// <param name="options">Emission options (namespace,
        /// pinning version, etc.). Null → defaults.</param>
        public static IReadOnlyList<EmittedSource> EmitFromText(
            string witSource, EmitOptions? options = null)
        {
            if (witSource == null) throw new ArgumentNullException(nameof(witSource));
            var doc = WitParser.Parse(witSource);
            var packages = WitToTypes.Convert(doc);
            return EmitFirstWorld(packages, options);
        }

        /// <summary>
        /// Load + emit from a directory of <c>.wit</c> files.
        /// Mirrors <see cref="WitLoader.LoadDirectory"/> —
        /// header-less files get attributed to the package
        /// declared in their sibling files, deps/ folders recurse,
        /// duplicate package declarations merge by qualified name.
        /// </summary>
        public static IReadOnlyList<EmittedSource> EmitFromDirectory(
            string directory, EmitOptions? options = null)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            var packages = WitLoader.LoadDirectoryTree(directory);
            return EmitFirstWorld(packages, options);
        }

        /// <summary>
        /// Load + emit from a specific list of <c>.wit</c> file
        /// paths. For tests / consumers that want to control the
        /// file set explicitly without the directory-recursion
        /// behavior of <see cref="EmitFromDirectory"/>.
        /// </summary>
        public static IReadOnlyList<EmittedSource> EmitFromFiles(
            IEnumerable<string> witPaths, EmitOptions? options = null)
        {
            if (witPaths == null) throw new ArgumentNullException(nameof(witPaths));
            var packages = WitLoader.LoadFiles(witPaths);
            return EmitFirstWorld(packages, options);
        }

        private static IReadOnlyList<EmittedSource> EmitFirstWorld(
            IReadOnlyList<CtPackage> packages, EmitOptions? options)
        {
            // Phase 1d v0: one world per generation pass — the
            // common case for an application binding to a single
            // .wit world. Multi-world / cross-package generation
            // is a follow-up; until then callers can iterate
            // packages themselves and call EmitWorld per world.
            foreach (var pkg in packages)
            {
                if (pkg.Worlds.Count == 0) continue;
                return CSharpEmitter.EmitWorld(pkg.Worlds[0], options);
            }
            throw new InvalidOperationException(
                "No worlds found across the supplied WIT inputs.");
        }

        /// <summary>
        /// Write the emission result to <paramref name="outDir"/>,
        /// one file per <see cref="EmittedSource"/>. Creates the
        /// directory if needed; overwrites existing files of the
        /// same name (consumers that want diff-or-write behavior
        /// can read the contents themselves and call this only
        /// when changes warrant it).
        /// </summary>
        public static void WriteToDirectory(
            IEnumerable<EmittedSource> sources, string outDir)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (outDir == null) throw new ArgumentNullException(nameof(outDir));
            Directory.CreateDirectory(outDir);
            foreach (var s in sources)
                File.WriteAllText(Path.Combine(outDir, s.FileName), s.Content);
        }
    }
}
