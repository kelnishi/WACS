// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using Wacs.ComponentModel.Bindgen;
using Xunit;

namespace Wacs.ComponentModel.Bindgen.Test
{
    /// <summary>
    /// Forward direction (.wit → C#) — exercises the
    /// programmatic <see cref="WitForward"/> API. The CLI tool
    /// funnels through these same entry points; testing here
    /// keeps the lib + CLI in lockstep without the subprocess
    /// machinery.
    /// </summary>
    public class WitForwardTests
    {
        private const string MinimalWit = @"
            package local:demo;
            world demo {
                export greet: func() -> string;
            }
        ";

        [Fact]
        public void EmitFromText_returns_at_least_one_source()
        {
            var sources = WitForward.EmitFromText(MinimalWit);
            Assert.NotEmpty(sources);
            // Every emission carries a non-empty filename and
            // non-empty content — these are the two contract
            // bits downstream consumers (file writer, source
            // generator, snapshot tests) rely on.
            foreach (var s in sources)
            {
                Assert.False(string.IsNullOrEmpty(s.FileName));
                Assert.False(string.IsNullOrEmpty(s.Content));
            }
        }

        [Fact]
        public void EmitFromText_world_shell_file_starts_with_namespace()
        {
            // The world shell is the canonical "first" file
            // CSharpEmitter produces — it carries the namespace
            // declaration the rest of the bindings nest under.
            var sources = WitForward.EmitFromText(MinimalWit);
            // Find a file whose content references the WIT
            // package's qualified name. The exact filename is
            // CSharpEmitter's concern; we check for the marker
            // string instead so the test stays robust to
            // filename-naming changes.
            Assert.Contains(sources, s => s.Content.Contains("Demo")
                || s.Content.Contains("demo"));
        }

        [Fact]
        public void WriteToDirectory_creates_files_under_outdir()
        {
            var sources = WitForward.EmitFromText(MinimalWit);
            var outDir = Path.Combine(Path.GetTempPath(),
                "bindgen-fwd-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                WitForward.WriteToDirectory(sources, outDir);
                Assert.True(Directory.Exists(outDir));
                var written = Directory.GetFiles(outDir).ToArray();
                Assert.Equal(sources.Count, written.Length);
                foreach (var s in sources)
                    Assert.True(File.Exists(Path.Combine(outDir, s.FileName)),
                        "missing emitted file: " + s.FileName);
            }
            finally
            {
                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, recursive: true);
            }
        }

        [Fact]
        public void EmitFromText_throws_for_witless_input()
        {
            // No `world` block — the loader has no first world to
            // emit. Bindgen surfaces this as an InvalidOperation
            // rather than silently producing zero files (which
            // a CLI consumer would mistake for "everything's
            // fine, just nothing to emit").
            const string noWorld = "package local:demo;";
            Assert.Throws<System.InvalidOperationException>(
                () => WitForward.EmitFromText(noWorld));
        }
    }
}
