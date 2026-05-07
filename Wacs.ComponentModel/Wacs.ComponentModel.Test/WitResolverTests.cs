// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 1a.2 tests for the cross-file <see cref="WitResolver"/>.
    /// Verifies binding of <see cref="CtExternInterfaceRef.Target"/>
    /// (world-scope refs to named interfaces) and
    /// <see cref="CtTypeRef.Target"/> on <c>use</c> imports inside
    /// an interface.
    /// </summary>
    public class WitResolverTests
    {
        private static IReadOnlyList<CtPackage> ParseAndConvert(string src) =>
            WitToTypes.Convert(WitParser.Parse(src));

        [Fact]
        public void Extern_interface_ref_binds_when_target_in_input_set()
        {
            // Two packages in one document — a "consumer" world in one,
            // a "provider" interface in the other.
            var providerSrc = @"
                package local:provider;
                interface widgets {
                    make: func() -> u32;
                }";
            var consumerSrc = @"
                package local:consumer;
                world app {
                    import local:provider/widgets;
                }";

            var packages = new List<CtPackage>();
            packages.AddRange(ParseAndConvert(providerSrc));
            packages.AddRange(ParseAndConvert(consumerSrc));

            // Pre-resolution: the import ref's Target is null.
            var consumerPkg = packages.First(p => p.Name.Namespace == "local"
                && p.Name.Path.Single() == "consumer");
            var world = consumerPkg.Worlds.Single();
            var import = world.Imports.Single();
            var iref = Assert.IsType<CtExternInterfaceRef>(import.Spec);
            Assert.Null(iref.Target);

            WitResolver.Resolve(packages);

            // Post-resolution: Target points at the provider interface.
            Assert.NotNull(iref.Target);
            Assert.Equal("widgets", iref.Target!.Name);
            Assert.Equal("local", iref.Target.Package!.Namespace);
            Assert.Equal(new[] { "provider" }, iref.Target.Package.Path);
        }

        [Fact]
        public void Extern_interface_ref_stays_unbound_when_target_missing()
        {
            // Consumer imports an interface from a package not in the
            // input set — resolver should leave Target null.
            var consumerSrc = @"
                package local:consumer;
                world app {
                    import external:unknown/iface;
                }";
            var packages = ParseAndConvert(consumerSrc);

            WitResolver.Resolve(packages);

            var world = packages.Single().Worlds.Single();
            var iref = Assert.IsType<CtExternInterfaceRef>(
                world.Imports.Single().Spec);
            Assert.Null(iref.Target);
        }

        [Fact]
        public void Resolve_is_idempotent()
        {
            var providerSrc = "package local:provider; interface iface { do: func() -> u32; }";
            var consumerSrc = "package local:consumer; world app { import local:provider/iface; }";
            var packages = new List<CtPackage>();
            packages.AddRange(ParseAndConvert(providerSrc));
            packages.AddRange(ParseAndConvert(consumerSrc));

            WitResolver.Resolve(packages);
            var consumer = packages.First(p => p.Name.Path.Single() == "consumer");
            var firstTarget = ((CtExternInterfaceRef)consumer.Worlds.Single()
                                .Imports.Single().Spec).Target;
            Assert.NotNull(firstTarget);

            WitResolver.Resolve(packages);
            var secondTarget = ((CtExternInterfaceRef)consumer.Worlds.Single()
                                .Imports.Single().Spec).Target;
            Assert.Same(firstTarget, secondTarget);
        }

        [Fact]
        public void Versioned_ref_matches_exact_version_string()
        {
            var providerSrc = @"
                package wasi:io@0.2.3;
                interface streams {
                    dummy: func() -> u32;
                }";
            var consumerSrc = @"
                package local:consumer;
                world app {
                    import wasi:io/streams@0.2.3;
                }";
            var packages = new List<CtPackage>();
            packages.AddRange(ParseAndConvert(providerSrc));
            packages.AddRange(ParseAndConvert(consumerSrc));

            WitResolver.Resolve(packages);
            var consumer = packages.First(p => p.Name.Path.Single() == "consumer");
            var iref = (CtExternInterfaceRef)consumer.Worlds.Single()
                            .Imports.Single().Spec;
            Assert.NotNull(iref.Target);
        }

        [Fact]
        public void Versioned_ref_does_not_match_different_patch_on_0x()
        {
            // 0.x versions are exact-match per the scope plan. 0.2.2
            // consumer can't bind to 0.2.3 provider.
            var providerSrc = @"
                package wasi:io@0.2.3;
                interface streams { dummy: func() -> u32; }";
            var consumerSrc = @"
                package local:consumer;
                world app { import wasi:io/streams@0.2.2; }";
            var packages = new List<CtPackage>();
            packages.AddRange(ParseAndConvert(providerSrc));
            packages.AddRange(ParseAndConvert(consumerSrc));

            WitResolver.Resolve(packages);
            var consumer = packages.First(p => p.Name.Path.Single() == "consumer");
            var iref = (CtExternInterfaceRef)consumer.Worlds.Single()
                            .Imports.Single().Spec;
            Assert.Null(iref.Target);
        }

        // ---- Fixture end-to-end --------------------------------------------

        private static string FindSubmoduleRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return dir == null ? string.Empty
                : Path.Combine(dir.FullName, "Spec.Test", "components");
        }

        [Fact]
        public void Resolve_helloworld_import_binds_to_wasi_cli_run()
        {
            // End-to-end: parse hello.wit + wasi-cli/run.wit from the
            // submodule, build the package set, resolve. The export
            // `wasi:cli/run@0.2.3` ref on hello's world should bind
            // to wasi-cli's run interface.
            var root = FindSubmoduleRoot();
            var helloWit = File.ReadAllText(Path.Combine(root,
                "fixtures", "hello-world", "wit", "hello.wit"));
            var runWit = File.ReadAllText(Path.Combine(root,
                "wasi-cli", "wit", "run.wit"));

            var packages = new List<CtPackage>();
            packages.AddRange(ParseAndConvert(helloWit));
            // run.wit has no package header — WitParser creates an
            // anonymous package. Give it the right context by
            // prepending the wasi-cli package declaration.
            packages.AddRange(ParseAndConvert(
                "package wasi:cli@0.2.3;\n" + runWit));

            WitResolver.Resolve(packages);

            var helloPkg = packages.First(p => p.Name.Namespace == "local"
                && p.Name.Path.Single() == "hello");
            var world = helloPkg.Worlds.Single();
            // The hello world has 2 imports (wasi:cli/stdout,
            // wasi:io/streams) and 1 export (wasi:cli/run).
            var runExport = world.Exports.Single();
            var iref = (CtExternInterfaceRef)runExport.Spec;
            Assert.NotNull(iref.Target);
            Assert.Equal("run", iref.Target!.Name);
        }
    }
}
