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

        // Files that exercise spec extensions requiring specialized lexer
        // grammar beyond core WAT. Skipping here lets the smoke test remain
        // a faithful gate for Phase 1.1 / 1.2 while we track the gap.
        private static readonly HashSet<string> SkipList = new HashSet<string>
        {
            // Custom annotations proposal: the `(@name ...)` grammar permits
            // arbitrary tokens (braces, commas, unmatched quotes etc.) inside
            // the annotation body — core idchar rules don't apply.
            "annotations.wast",
        };

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
    }
}
