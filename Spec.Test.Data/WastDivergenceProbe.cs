// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Text;

namespace Spec.Test
{
    /// <summary>
    /// Diagnostic helper for the WAST → in-memory adapter migration.
    /// For a given .wast file, compares two parse paths:
    ///   1. WACS native: <see cref="TextScriptParser.ParseWast"/> + the
    ///      adapter, with each module's <see cref="Module.Validate"/>.
    ///   2. wabt baseline: the JSON + .wasm artifacts under
    ///      <c>generated-json/&lt;name&gt;.wast/</c>, parsed via
    ///      <see cref="BinaryModuleParser.ParseWasm"/> + Validate.
    ///
    /// Reports the first module-level disagreement (validation result
    /// flips, or one path throws while the other succeeds). Used
    /// interactively while closing parser gaps; not part of the
    /// regular test rotation.
    /// </summary>
    public static class WastDivergenceProbe
    {
        public sealed class Divergence
        {
            public int ModuleIndex;
            public int Line;
            public string? WacsResult;
            public string? WabtResult;
            public override string ToString() =>
                $"module #{ModuleIndex} (line {Line}): WACS={WacsResult}, wabt={WabtResult}";
        }

        public static IReadOnlyList<Divergence> Compare(
            string wastPath, string wabtJsonDir)
        {
            var divs = new List<Divergence>();

            BinaryModuleParser.ParseBranchHints = true;

            // ---- WACS path ----
            var script = TextScriptParser.ParseWast(File.ReadAllText(wastPath));
            var wacsResults = new List<(int Line, string Result)>();
            int wacsModIdx = 0;
            foreach (var cmd in script)
            {
                if (cmd is ScriptModule sm)
                {
                    wacsResults.Add((sm.Line, ResultOf(() =>
                    {
                        if (sm.Module == null) throw new FormatException("WAT parse failed");
                        var v = sm.Module.Validate();
                        return v.IsValid ? "valid" : "invalid: " + Trunc(string.Join("|",
                            v.Errors.Take(2).Select(e => e.ErrorMessage)));
                    })));
                    wacsModIdx++;
                }
            }

            // ---- wabt baseline ----
            var wabtResults = new List<(int Line, string Result)>();
            if (Directory.Exists(wabtJsonDir))
            {
                var jsonFiles = Directory.GetFiles(wabtJsonDir, "*.json");
                foreach (var jf in jsonFiles)
                {
                    var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jf));
                    foreach (var elem in doc.RootElement.GetProperty("commands").EnumerateArray())
                    {
                        var type = elem.GetProperty("type").GetString();
                        if (type != "module" && type != "module_definition") continue;
                        var line = elem.GetProperty("line").GetInt32();
                        if (!elem.TryGetProperty("filename", out var fnEl)) continue;
                        var wasmFile = Path.Combine(wabtJsonDir, fnEl.GetString()!);
                        wabtResults.Add((line, ResultOf(() =>
                        {
                            using var fs = File.OpenRead(wasmFile);
                            var module = BinaryModuleParser.ParseWasm(fs);
                            var v = module.Validate();
                            return v.IsValid ? "valid" : "invalid: " + Trunc(string.Join("|",
                                v.Errors.Take(2).Select(e => e.ErrorMessage)));
                        })));
                    }
                }
            }

            // ---- Pair by ordinal index ----
            int n = Math.Min(wacsResults.Count, wabtResults.Count);
            for (int i = 0; i < n; i++)
            {
                if (wacsResults[i].Result != wabtResults[i].Result)
                {
                    divs.Add(new Divergence
                    {
                        ModuleIndex = i,
                        Line = wacsResults[i].Line,
                        WacsResult = wacsResults[i].Result,
                        WabtResult = wabtResults[i].Result,
                    });
                }
            }
            if (wacsResults.Count != wabtResults.Count)
            {
                divs.Add(new Divergence
                {
                    ModuleIndex = -1,
                    Line = -1,
                    WacsResult = $"{wacsResults.Count} modules",
                    WabtResult = $"{wabtResults.Count} modules",
                });
            }
            return divs;
        }

        private static string ResultOf(Func<string> action)
        {
            try { return action(); }
            catch (ValidationException) { return "validation-exception"; }
            catch (FormatException e) { return "format-exception: " + Trunc(e.Message); }
            catch (Exception e) { return $"{e.GetType().Name}: {Trunc(e.Message)}"; }
        }

        private static string Trunc(string s) =>
            s.Length > 200 ? s.Substring(0, 200) + "…" : s;
    }
}
