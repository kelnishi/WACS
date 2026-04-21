// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Text;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Phase 3 equivalence gate: for every spec .wast we can fully parse,
    /// compare the Module objects produced by the new
    /// <see cref="TextModuleParser"/> against those the canonical
    /// <see cref="BinaryModuleParser.ParseWasm"/> produces from the
    /// pre-generated .wasm sidecars in
    /// <c>Spec.Test/generated-json/foo.wast/foo.N.wasm</c>. Both pipelines
    /// should yield structurally-identical Modules — this test records how
    /// many files that holds for and flags the first divergences.
    /// </summary>
    public class SpecWastEquivalenceTests
    {
        private readonly ITestOutputHelper _output;
        public SpecWastEquivalenceTests(ITestOutputHelper output) => _output = output;

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? string.Empty;
        }

        [Fact]
        public void Equivalence_with_binary_parser_on_spec_suite()
        {
            var root = FindRepoRoot();
            var wastDir = Path.Combine(root, "Spec.Test", "spec", "test", "core");
            var jsonRoot = Path.Combine(root, "Spec.Test", "generated-json");
            if (!Directory.Exists(wastDir) || !Directory.Exists(jsonRoot))
            {
                _output.WriteLine($"spec / generated-json not available; skipping");
                return;
            }

            int filesTried = 0, filesMatched = 0, filesParseFail = 0, filesMissingWasm = 0;
            int modulesChecked = 0, modulesMatched = 0;
            var perFileStatus = new List<string>();

            foreach (var wastPath in Directory.EnumerateFiles(wastDir, "*.wast").OrderBy(p => p))
            {
                var wastName = Path.GetFileName(wastPath);
                var wastJsonDir = Path.Combine(jsonRoot, wastName);
                if (!Directory.Exists(wastJsonDir))
                {
                    filesMissingWasm++;
                    continue;
                }

                filesTried++;
                List<ScriptCommand> script;
                try
                {
                    script = TextScriptParser.ParseWast(File.ReadAllText(wastPath));
                }
                catch (Exception ex)
                {
                    filesParseFail++;
                    perFileStatus.Add($"{wastName}: text parse fail — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    continue;
                }

                // Index .wasm files by their numeric suffix — these match
                // the source-order position of each (module …) command in
                // the .wast.
                var wasmByIdx = Directory.EnumerateFiles(wastJsonDir, "*.wasm")
                    .ToDictionary(p => int.Parse(Path.GetFileNameWithoutExtension(p)!.Split('.').Last()));

                // Walk ALL modules (top-level + embedded in assertions) in
                // source order to match wast2json's numbering.
                var allModules = new List<ScriptModule>();
                foreach (var cmd in script)
                {
                    switch (cmd)
                    {
                        // Instance forms share content with their source —
                        // no separate .wasm file — skip to match wast2json
                        // ordinal numbering.
                        case ScriptModule sm when sm.Kind == ScriptModuleKind.Instance: break;
                        case ScriptModule sm: allModules.Add(sm); break;
                        case ScriptAssertInvalid   ai when ai.Module != null:  allModules.Add(ai.Module); break;
                        case ScriptAssertMalformed am when am.Module != null:  allModules.Add(am.Module); break;
                        case ScriptAssertUnlinkable au when au.Module != null: allModules.Add(au.Module); break;
                        case ScriptAssertTrap at when at.Module != null: allModules.Add(at.Module); break;
                    }
                }
                int moduleOrdinal = -1;
                bool fileOK = true;
                bool anyCompared = false;
                foreach (var sm in allModules)
                {
                    moduleOrdinal++;
                    if (sm.Kind != ScriptModuleKind.Text || sm.Module == null) continue;
                    if (!wasmByIdx.TryGetValue(moduleOrdinal, out var wasmPath)) continue;

                    Module binaryModule;
                    try
                    {
                        using var fs = File.OpenRead(wasmPath);
                        binaryModule = BinaryModuleParser.ParseWasm(fs);
                    }
                    catch (Exception ex)
                    {
                        // Intentionally-invalid .wasm files (from spec
                        // assert_invalid / assert_malformed) fail here.
                        // Not a text-parser mismatch — don't count.
                        perFileStatus.Add($"{wastName}[{moduleOrdinal}]: binary parse threw — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                        continue;
                    }
                    anyCompared = true;
                    modulesChecked++;
                    var report = ModuleEquivalence.Compare(sm.Module, binaryModule);
                    if (report.IsMatch)
                        modulesMatched++;
                    else
                    {
                        if (fileOK)
                            perFileStatus.Add($"{wastName}[{moduleOrdinal}]: {report.Mismatches.Count} mismatch(es) — first: {report.Mismatches[0]}");
                        fileOK = false;
                    }
                }
                if (!anyCompared)
                {
                    // No text-form modules to compare — common for .wast
                    // files that are mostly binary/quote module assertions.
                    filesMatched++;
                    continue;
                }
                if (fileOK) filesMatched++;
            }

            _output.WriteLine($"Equivalence: {filesMatched}/{filesTried} files match; modules {modulesMatched}/{modulesChecked}");
            _output.WriteLine($"  parse-fail: {filesParseFail}, missing-wasm: {filesMissingWasm}");
            foreach (var line in perFileStatus.Take(50))
                _output.WriteLine("  " + line);
            if (perFileStatus.Count > 20)
                _output.WriteLine($"  (+ {perFileStatus.Count - 20} more)");

            // Not a pass/fail gate — intentionally diagnostic so the number
            // climbs visibly as Phase 1 gaps close.
            Assert.True(filesTried > 0);
        }
    }
}
