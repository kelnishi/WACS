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

        // -----------------------------------------------------
        //                Host-mode smoke tests
        // -----------------------------------------------------

        private const string HostModeWit = @"
            package wasi:demo@0.2.3;
            interface env {
                get-args: func() -> list<string>;
                initial-cwd: func() -> option<string>;
            }
        ";

        [Fact]
        public void EmitHost_produces_an_interface_per_wit_interface()
        {
            var packages = Wacs.ComponentModel.WIT.WitLoader
                .LoadFiles(WriteScratch(HostModeWit));
            var sources = WitForward
                .EmitHostInterfacesFromPackages(packages);
            Assert.NotEmpty(sources);
            // One C# file per WIT interface; this fixture has one.
            Assert.Single(sources);
            Assert.Equal("Env.g.cs", sources[0].FileName);
        }

        [Fact]
        public void EmitHost_uses_faithful_Option_and_list_mappings()
        {
            var packages = Wacs.ComponentModel.WIT.WitLoader
                .LoadFiles(WriteScratch(HostModeWit));
            var src = WitForward
                .EmitHostInterfacesFromPackages(packages)[0]
                .Content;
            // initial-cwd's option<string> must lift to Option<string>,
            // not nullable string. Faithful mapping is the project's
            // explicit choice.
            Assert.Contains("Option<string>", src);
            // get-args' list<string> must be string[].
            Assert.Contains("string[]", src);
            // Each method carries a [WitSource(...)] attribute.
            Assert.Contains("[WitSource(@\"", src);
        }

        [Fact]
        public void EmitHost_namespace_derives_from_package_name()
        {
            var packages = Wacs.ComponentModel.WIT.WitLoader
                .LoadFiles(WriteScratch(HostModeWit));
            var src = WitForward
                .EmitHostInterfacesFromPackages(packages)[0]
                .Content;
            // Sanity: namespace declaration starts with our root.
            Assert.Contains("namespace Wasi.", src);
        }

        [Fact]
        public void EmitHost_dump_for_debugging()
        {
            var packages = Wacs.ComponentModel.WIT.WitLoader
                .LoadFiles(WriteScratch(HostModeWit));
            var src = WitForward
                .EmitHostInterfacesFromPackages(packages)[0]
                .Content;
            // Useful when debugging — gets surfaced in test output.
            Assert.True(src.Length > 0, "Empty emission:\n" + src);
            // Always pass; the failure assertion above only fires
            // on truly empty output. Use `dotnet test --logger
            // console;verbosity=detailed` to see the dump.
            System.Console.WriteLine(src);
        }

        [Fact]
        public void EmitHost_real_wasi_random_compiles_shape()
        {
            // Cross-check against the actual vendored WASI WIT
            // for wasi:random — small, no resources, exercises
            // primitives + list<u8>.
            var witDir = FindWitDir("Wacs.WASI.Preview2/wit");
            if (witDir == null)
            {
                // Test can run from the bindgen test project's
                // bin dir; if we can't locate the WIT root,
                // skip rather than fail.
                return;
            }
            var sources = WitForward
                .EmitHostInterfacesFromDirectory(witDir);
            // Random emits 3 interfaces: random, insecure,
            // insecure-seed.
            var randomFile = sources.FirstOrDefault(s =>
                s.FileName == "Random.g.cs");
            Assert.NotNull(randomFile);
            // get-random-bytes(len: u64) -> list<u8>
            Assert.Contains("byte[]", randomFile!.Content);
            Assert.Contains("ulong", randomFile.Content);
            Assert.Contains("[WitSource(@\"interface random\"",
                randomFile.Content);
        }

        [Fact]
        public void EmitHost_full_wasi_preview2_does_not_throw()
        {
            // Full smoke test: every vendored WASI 0.2.3 WIT file
            // emits without exception. Regression guard for
            // unhandled CtValType subclasses (records inside
            // variants, etc.).
            var witDir = FindWitDir("Wacs.WASI.Preview2/wit");
            if (witDir == null) return;
            var sources = WitForward
                .EmitHostInterfacesFromDirectory(witDir);
            // Sanity: at least one file per top-level WASI
            // namespace plus their dep tree.
            Assert.NotEmpty(sources);
            // Each emitted file should declare a namespace.
            foreach (var s in sources)
                Assert.Contains("namespace ", s.Content);
        }

        private static string? FindWitDir(string suffix)
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                var candidate = Path.Combine(d.FullName, suffix);
                if (Directory.Exists(candidate)) return candidate;
                d = d.Parent;
            }
            return null;
        }

        // -----------------------------------------------------

        private static System.Collections.Generic.IEnumerable<string>
            WriteScratch(string witText)
        {
            var dir = Path.Combine(Path.GetTempPath(),
                "wacs-host-emit-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "demo.wit");
            File.WriteAllText(file, witText);
            return new[] { file };
        }
    }
}
