// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Wacs.ComponentModel.Validation;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Random;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Phase C: validation layer. Verifies the
    /// <see cref="Linker"/> + <see cref="WitContract"/> pair
    /// catches contract drift across the WASIp2 binding
    /// surface.
    /// </summary>
    public class ValidationTests
    {
        private static string FindWitDir()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(
                dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName,
                "Wacs.WASI.Preview2", "wit");
        }

        // ---------- Linker manifest tracking --------------------

        [Fact]
        public void Linker_records_bindings_added_through_Bind_call()
        {
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime);
            var resources = new ResourceContext();
            linker.Bind(new RandomBindings(new Random.Random()));

            // wasi:random/random has 2 free functions:
            // get-random-bytes, get-random-u64.
            var randomKeys = linker.Bindings
                .Where(b => b.Module == "wasi:random/random@0.2.3")
                .Select(b => b.Entity)
                .ToHashSet();
            Assert.Contains("get-random-bytes", randomKeys);
            Assert.Contains("get-random-u64", randomKeys);
        }

        // ---------- ValidationLevel.Off short-circuits ----------

        [Fact]
        public void Validation_Off_returns_clean_report_without_inspection()
        {
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime, ValidationLevel.Off);
            // Don't bind anything; the contract requires
            // wasi:random/random imports — but Off skips checks.
            var contract = WitContract.FromText(@"
                package wasi:random@0.2.3;
                interface random {
                    get-random-bytes: func(len: u64) -> list<u8>;
                }
            ");
            var report = linker.Validate(contract);
            Assert.True(report.IsClean);
        }

        // ---------- Warnings collects issues --------------------

        [Fact]
        public void Validation_Warnings_reports_missing_bindings()
        {
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime, ValidationLevel.Warnings);
            // No bindings registered; contract demands one.
            var contract = WitContract.FromText(@"
                package wasi:random@0.2.3;
                interface random {
                    get-random-bytes: func(len: u64) -> list<u8>;
                    get-random-u64: func() -> u64;
                }
            ");
            var report = linker.Validate(contract);
            Assert.False(report.IsClean);
            Assert.Equal(2, report.Issues.Count);
            Assert.All(report.Issues, i =>
                Assert.Equal(ValidationIssueKind.MissingBinding,
                    i.Kind));
        }

        [Fact]
        public void Validation_Warnings_reports_clean_when_bindings_match()
        {
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime, ValidationLevel.Warnings);
            linker.Bind(new RandomBindings(new Random.Random()));

            var contract = WitContract.FromText(@"
                package wasi:random@0.2.3;
                interface random {
                    get-random-bytes: func(len: u64) -> list<u8>;
                    get-random-u64: func() -> u64;
                }
            ");
            var report = linker.Validate(contract);
            Assert.True(report.IsClean,
                "expected clean, got: " + report);
        }

        // ---------- Strict throws on first issue ----------------

        [Fact]
        public void Validation_Strict_throws_on_missing_binding()
        {
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime, ValidationLevel.Strict);

            var contract = WitContract.FromText(@"
                package wasi:random@0.2.3;
                interface random {
                    get-random-bytes: func(len: u64) -> list<u8>;
                }
            ");
            var ex = Assert.Throws<ValidationException>(
                () => linker.Validate(contract));
            Assert.Single(ex.Report.Issues);
            Assert.Equal(ValidationIssueKind.MissingBinding,
                ex.Report.Issues[0].Kind);
        }

        // ---------- Resource-drop bookkeeping is filtered out ---

        [Fact]
        public void Validation_does_not_flag_resource_drop_bindings_as_extra()
        {
            // IoBindings binds [resource-drop]error and other
            // drops; the contract for wasi:io/error has no entry
            // for [resource-drop]X (the WIT spec doesn't list
            // them — drop is implicit). Validation should treat
            // these as bookkeeping, not extras.
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime, ValidationLevel.Warnings);
            var resources = new ResourceContext();
            linker.Bind(new IoBindings(resources));

            // Empty contract — every binding should be flagged
            // as Extra unless it's a [resource-drop].
            var contract = new WitContract(
                System.Array.Empty<ImportEntry>());
            var report = linker.Validate(contract);

            Assert.DoesNotContain(report.Issues, i =>
                i.Entity.StartsWith("[resource-drop]"));
        }

        // ---------- Real WASI WIT tree round-trip ---------------

        [Fact]
        public void Validation_against_full_wasi_directory_loads_clean()
        {
            // Sanity: we can build a contract for the entire
            // vendored WASI tree without the parser blowing up.
            var witDir = FindWitDir();
            var contract = WitContract.FromDirectory(witDir);
            Assert.NotEmpty(contract.Imports);
            // wasi-cli, wasi-clocks, wasi-filesystem,
            // wasi-http, wasi-io, wasi-random, wasi-sockets:
            // every one should contribute imports.
            var modules = contract.Imports
                .Select(i => i.Module).Distinct().ToList();
            Assert.Contains(modules, m =>
                m.StartsWith("wasi:cli/"));
            Assert.Contains(modules, m =>
                m.StartsWith("wasi:io/"));
            Assert.Contains(modules, m =>
                m.StartsWith("wasi:http/"));
        }

        // ---------- Assembly-embedded WIT resources --------------

        [Fact]
        public void Validation_FromAssembly_loads_WASI_contract_from_DLL()
        {
            // The WIT files are embedded as <EmbeddedResource>
            // with logical names "wit/...". Verify the runtime
            // can reconstitute the full contract from the
            // shipped DLL alone — no source-tree access.
            var asm = typeof(Wacs.WASI.Preview2.Cli.CliBindings)
                .Assembly;
            var contract = WitContract.FromAssembly(asm);
            Assert.NotEmpty(contract.Imports);
            // Same coverage assertion as the directory variant.
            var modules = contract.Imports
                .Select(i => i.Module).Distinct().ToList();
            Assert.Contains(modules, m =>
                m.StartsWith("wasi:cli/"));
            Assert.Contains(modules, m =>
                m.StartsWith("wasi:http/"));
            Assert.Contains(modules, m =>
                m.StartsWith("wasi:sockets/"));
        }

        [Fact]
        public void ReadEmbeddedWit_enumerates_every_wit_resource()
        {
            var asm = typeof(Wacs.WASI.Preview2.Cli.CliBindings)
                .Assembly;
            var sources = WitContract.ReadEmbeddedWit(asm);
            Assert.NotEmpty(sources);
            // Every shipped WIT file is exposed by logical
            // name + readable as text.
            Assert.All(sources, s =>
            {
                Assert.StartsWith("wit/", s.Name);
                Assert.EndsWith(".wit", s.Name);
                Assert.False(string.IsNullOrEmpty(s.Text));
            });
            // Spot-check: random.wit is in the set.
            Assert.Contains(sources, s =>
                s.Name == "wit/deps/random/random.wit");
        }

        [Fact]
        public void Validation_FromAssembly_catches_missing_random_binding()
        {
            // End-to-end: embed-driven contract + a linker that
            // bound nothing yields the expected MissingBinding
            // diagnostics. Confirms the embedded WIT survives
            // round-trip without dropping interfaces.
            var runtime = new WasmRuntime();
            var linker = new Linker(runtime, ValidationLevel.Warnings);
            var asm = typeof(Wacs.WASI.Preview2.Random.Random)
                .Assembly;
            var contract = WitContract.FromAssembly(asm);
            var report = linker.Validate(contract);

            Assert.False(report.IsClean);
            Assert.Contains(report.Issues, i =>
                i.Kind == ValidationIssueKind.MissingBinding
                && i.Module == "wasi:random/random@0.2.3"
                && i.Entity == "get-random-bytes");
        }

        // ---------- FromWorld scoping ---------------------------

        [Fact]
        public void FromWorld_scopes_imports_to_a_specific_world()
        {
            // wasi:cli/imports is a pure-imports world — every
            // entry should be host-bindable. wasi:cli/command
            // pulls those in via `include imports` and adds
            // `export run` (guest-side, not in our contract).
            var asm = typeof(Wacs.WASI.Preview2.Cli.CliBindings)
                .Assembly;
            var packages = WitContract.LoadAssemblyPackages(asm);

            var importsContract = WitContract.FromWorld(packages,
                "wasi:cli/imports@0.2.3");
            var commandContract = WitContract.FromWorld(packages,
                "wasi:cli/command@0.2.3");

            // Both worlds should pull in CLI + clocks + io +
            // random + sockets + filesystem imports.
            foreach (var c in new[] { importsContract,
                commandContract })
            {
                var modules = c.Imports.Select(i => i.Module)
                    .Distinct().ToList();
                Assert.Contains(modules, m =>
                    m == "wasi:cli/environment@0.2.3");
                Assert.Contains(modules, m =>
                    m == "wasi:io/streams@0.2.3");
                Assert.Contains(modules, m =>
                    m == "wasi:filesystem/types@0.2.3");
            }

            // Neither contract should include guest-export
            // interfaces — wasi:cli/run is an EXPORT of the
            // command world, not an import.
            foreach (var c in new[] { importsContract,
                commandContract })
            {
                Assert.DoesNotContain(c.Imports, i =>
                    i.Module == "wasi:cli/run@0.2.3");
            }
        }

        [Fact]
        public void FromWorld_throws_for_missing_world_name()
        {
            var asm = typeof(Wacs.WASI.Preview2.Cli.CliBindings)
                .Assembly;
            var packages = WitContract.LoadAssemblyPackages(asm);
            Assert.Throws<System.InvalidOperationException>(
                () => WitContract.FromWorld(packages,
                    "wasi:nonexistent/world@9.9.9"));
        }

        // Strict canon-ABI lock: with import-side scoping in
        // BuildFromPackages, FromAssembly's contract excludes
        // export-only interfaces (e.g. wasi:cli/run) so the
        // DI-bound linker matches the contract exactly — zero
        // drift expected. Any future ArityMismatch /
        // ReturnTypeMismatch / ExtraBinding / MissingBinding
        // surfaces as a real regression.
        [Fact]
        public void FromAssembly_excludes_export_only_interfaces()
        {
            // wasi:cli/run is a guest export interface
            // (defined in run.wit, exported by the wasi:cli/
            // command world). Hosts don't bind it, so it must
            // not appear in the FromAssembly contract.
            var asm = typeof(Wacs.WASI.Preview2.Cli.CliBindings)
                .Assembly;
            var contract = WitContract.FromAssembly(asm);
            Assert.DoesNotContain(contract.Imports, i =>
                i.Module == "wasi:cli/run@0.2.3");
        }

        [Fact]
        public void Strict_canon_abi_drift_is_bounded()
        {
            var services = new Microsoft.Extensions.DependencyInjection
                .ServiceCollection();
            services.AddWasiPreview2(opts =>
                opts.ValidationLevel = ValidationLevel.Warnings);
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var linker = scope.ServiceProvider
                .GetRequiredService<Linker>();
            var asm = typeof(Wacs.WASI.Preview2.Cli.CliBindings).Assembly;
            var report = linker.Validate(WitContract.FromAssembly(asm));

            Assert.True(report.IsClean,
                "Unexpected canon-ABI drift: "
                + string.Join("; ", report.Issues.Select(i =>
                    i.Kind + " " + i.Module + "/" + i.Entity)));
        }

        [Fact]
        public void FromWorld_validation_passes_imports_world_with_DI()
        {
            // The DI integration's pre-bound Linker should
            // satisfy the wasi:cli/imports world contract
            // exactly — every import that world declares is
            // bound; no guest-export pollution.
            var services = new Microsoft.Extensions.DependencyInjection
                .ServiceCollection();
            services.AddWasiPreview2(opts =>
                opts.ValidationLevel = ValidationLevel.Warnings);
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var linker = scope.ServiceProvider
                .GetRequiredService<Linker>();
            var packages = WitContract.LoadAssemblyPackages(
                typeof(Wacs.WASI.Preview2.Cli.CliBindings).Assembly);
            var contract = WitContract.FromWorld(packages,
                "wasi:cli/imports@0.2.3");

            // Validate; cli/imports world's bindings should
            // all be present even if some arity-mismatch noise
            // remains from the validator's coarse estimation.
            var report = linker.Validate(contract);
            Assert.DoesNotContain(report.Issues, i =>
                i.Kind == ValidationIssueKind.MissingBinding);
        }
    }
}
