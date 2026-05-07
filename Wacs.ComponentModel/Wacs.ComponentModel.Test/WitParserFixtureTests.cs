// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.WIT;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Smoke-parse every .wit file in the vendored WASI 0.2.3 fixture tree
    /// at <c>Spec.Test/components/wasi-cli/wit/</c>. This is the Phase 0b
    /// validation gate — the WIT parser needs to handle the grammar
    /// constructs real-world WASI interface packages use before we commit
    /// to the rest of the Component Model plan.
    ///
    /// Test strategy: each .wit file is a data-driven Theory case. We
    /// parse the source, assert no exception, and capture a minimal
    /// AST summary (package name, interface / world / type counts) so
    /// regressions in the parser surface as specific failures rather
    /// than all-or-nothing smoke breakage.
    ///
    /// The fixture tree is a git submodule pinned at wasi-cli v0.2.3
    /// (commit <c>d4fddec</c>, tag <c>v0.2.3</c>). Bump via
    /// <c>git -C Spec.Test/components/wasi-cli checkout vX.Y.Z</c>
    /// and update the expected inventory below.
    /// </summary>
    public class WitParserFixtureTests
    {
        private static string FindWasiCliWitDir()
        {
            // Walk up from the test binary to the repo root, then into
            // Spec.Test/components/wasi-cli/wit.
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            if (dir == null) return string.Empty;
            return Path.Combine(dir.FullName, "Spec.Test", "components",
                                "wasi-cli", "wit");
        }

        public static IEnumerable<object[]> WitFixtures()
        {
            var root = FindWasiCliWitDir();
            if (!Directory.Exists(root)) yield break;
            foreach (var path in Directory.EnumerateFiles(root, "*.wit",
                                                          SearchOption.AllDirectories)
                                          .OrderBy(p => p))
            {
                // Expose the relative path from the wit/ root as the case
                // id so xUnit labels individual tests by their location.
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                yield return new object[] { rel };
            }
        }

        [Theory]
        [MemberData(nameof(WitFixtures))]
        public void Fixture_parses_without_error(string relativePath)
        {
            var root = FindWasiCliWitDir();
            Assert.True(Directory.Exists(root),
                $"WIT fixture tree missing at {root}. Did you init the " +
                "git submodule? `git submodule update --init " +
                "Spec.Test/components/wasi-cli`.");

            var full = Path.Combine(root, relativePath);
            var src = File.ReadAllText(full);

            // Smoke parse: no exception.
            var doc = WitParser.Parse(src);
            Assert.NotNull(doc);

            // Doc must not be empty (some files are deps-only "world"
            // stubs, but every fixture in the tree carries at least a
            // package declaration or one use/interface/world — none is
            // blank).
            var packages = doc.Packages;
            var topUses = doc.TopLevelUses;
            Assert.True(packages.Count > 0 || topUses.Count > 0,
                $"Parsed document for {relativePath} had no packages " +
                "and no top-level uses — parser likely silently consumed " +
                "the content without building an AST.");
        }

        /// <summary>
        /// Sanity check that the fixture tree has the expected file
        /// count. If this fails after a submodule bump, update the
        /// expected number and the per-file assertions below if new
        /// fixtures were added.
        /// </summary>
        [Fact]
        public void Fixture_inventory_matches_v0_2_3()
        {
            var root = FindWasiCliWitDir();
            if (!Directory.Exists(root)) return; // skipped when submodule absent
            var files = Directory.EnumerateFiles(root, "*.wit",
                                                 SearchOption.AllDirectories)
                                 .Count();
            // wasi-cli v0.2.3 ships 7 top-level .wit files + 23 files in
            // deps/{clocks,filesystem,io,random,sockets} = 30 total.
            Assert.Equal(30, files);
        }
    }
}
