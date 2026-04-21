// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Smoke-test: every .wast file in the spec core suite must tokenize and
    /// parse as an s-expression tree without error. WASM semantics are out of
    /// scope — this only exercises Lexer + SExprParser.
    /// </summary>
    public class SpecWastSmokeTests
    {
        private static string FindSpecCoreDir()
        {
            // Walk up from the test binary to the repo root, then into
            // Spec.Test/spec/test/core.
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            if (dir == null) return string.Empty;
            return Path.Combine(dir.FullName, "Spec.Test", "spec", "test", "core");
        }

        /// <summary>
        /// Currently empty — the lexer handles the annotations proposal
        /// directly (broadens idchars inside <c>(@name …)</c>).
        /// </summary>
        private static readonly HashSet<string> SkipList = new HashSet<string>();

        public static IEnumerable<object[]> CoreWastFiles()
        {
            var dir = FindSpecCoreDir();
            if (!Directory.Exists(dir)) yield break;
            foreach (var path in Directory.EnumerateFiles(dir, "*.wast").OrderBy(p => p))
            {
                var name = Path.GetFileName(path);
                if (SkipList.Contains(name)) continue;
                yield return new object[] { name };
            }
        }

        [Theory]
        [MemberData(nameof(CoreWastFiles))]
        public void Spec_wast_file_parses_as_sexpr(string filename)
        {
            var dir = FindSpecCoreDir();
            var full = Path.Combine(dir, filename);
            var src = File.ReadAllText(full);
            var top = SExprParser.Parse(src);
            Assert.NotEmpty(top);
            // Each top-level form must be a list (.wast files are sequences of
            // parenthesized commands).
            foreach (var node in top)
                Assert.Equal(SExprKind.List, node.Kind);
        }

        /// <summary>
        /// Full-parse gauge: try to run the entire <see cref="TextScriptParser"/>
        /// over each spec .wast. Tracks how many files make it through without
        /// throwing. Not an equality assertion — the phase 1.4 parser is an
        /// MVP (no memargs, br_table, SIMD, GC, etc.), so many files will
        /// fail at the instruction layer. The test records the count for
        /// visibility.
        /// </summary>
        [Fact]
        public void Spec_wast_full_parse_coverage()
        {
            var dir = FindSpecCoreDir();
            if (!Directory.Exists(dir)) return;   // spec checkout absent
            int total = 0, ok = 0;
            var failures = new List<string>();
            foreach (var path in Directory.EnumerateFiles(dir, "*.wast").OrderBy(p => p))
            {
                var name = Path.GetFileName(path);
                if (SkipList.Contains(name)) continue;
                total++;
                try
                {
                    var src = File.ReadAllText(path);
                    Wacs.Core.Text.TextScriptParser.ParseWast(src);
                    ok++;
                }
                catch (System.Exception ex)
                {
                    var firstLine = ex.Message.Split('\n')[0];
                    var stackLines = ex.StackTrace?.Split('\n') ?? new string[0];
                    // Show the first 3 stack frames for context.
                    var head = string.Join(" -> ", stackLines.Take(3).Select(s => s.Trim()));
                    failures.Add($"{name}: {ex.GetType().Name}: {firstLine} [at {head}]");
                }
            }
            // Emit diagnostic counts; don't fail — Phase 1 is incremental.
            // If you want to see which files fail, uncomment the next line
            // or filter to individual cases.
            System.Console.WriteLine($"Spec full-parse: {ok}/{total} OK");
            foreach (var f in failures.Take(120))
                System.Console.WriteLine("  " + f);
            Assert.True(total > 0);
        }
    }
}
