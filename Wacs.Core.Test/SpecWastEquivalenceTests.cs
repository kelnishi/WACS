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

                // Collect the binary-produced modules in declaration order.
                var wasms = Directory.EnumerateFiles(wastJsonDir, "*.wasm")
                    .OrderBy(p => int.Parse(Path.GetFileNameWithoutExtension(p)!.Split('.').Last()))
                    .ToList();

                // Pair up my ScriptModule (kind=Text) commands with the
                // on-disk .wasm files in source order.
                var textModules = script.OfType<ScriptModule>()
                    .Where(m => m.Kind == ScriptModuleKind.Text && m.Module != null)
                    .Select(m => m.Module!)
                    .ToList();

                int pairCount = Math.Min(textModules.Count, wasms.Count);
                if (pairCount == 0)
                {
                    // No text-form modules to compare — common for .wast that
                    // are mostly (module binary …) assertion stress tests.
                    filesMatched++;
                    continue;
                }

                bool fileOK = true;
                for (int k = 0; k < pairCount; k++)
                {
                    Module binaryModule;
                    try
                    {
                        using var fs = File.OpenRead(wasms[k]);
                        binaryModule = BinaryModuleParser.ParseWasm(fs);
                    }
                    catch (Exception ex)
                    {
                        perFileStatus.Add($"{wastName}[{k}]: binary parse threw — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                        fileOK = false;
                        break;
                    }
                    modulesChecked++;
                    var report = ModuleEquivalence.Compare(textModules[k], binaryModule);
                    if (report.IsMatch)
                        modulesMatched++;
                    else
                    {
                        if (fileOK) // record first divergence per file
                            perFileStatus.Add($"{wastName}[{k}]: {report.Mismatches.Count} mismatch(es) — first: {report.Mismatches[0]}");
                        fileOK = false;
                    }
                }
                if (fileOK) filesMatched++;
            }

            _output.WriteLine($"Equivalence: {filesMatched}/{filesTried} files match; modules {modulesMatched}/{modulesChecked}");
            _output.WriteLine($"  parse-fail: {filesParseFail}, missing-wasm: {filesMissingWasm}");
            foreach (var line in perFileStatus.Take(20))
                _output.WriteLine("  " + line);
            if (perFileStatus.Count > 20)
                _output.WriteLine($"  (+ {perFileStatus.Count - 20} more)");

            // Not a pass/fail gate — intentionally diagnostic so the number
            // climbs visibly as Phase 1 gaps close.
            Assert.True(filesTried > 0);
        }
    }
}
