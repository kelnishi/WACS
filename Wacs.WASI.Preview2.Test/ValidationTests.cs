// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using Wacs.ComponentModel.Validation;
using Wacs.Core.Runtime;
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
    }
}
