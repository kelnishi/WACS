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
            // Each entry collects modules in order — those declared at
            // top level AND those embedded in assert_invalid /
            // assert_malformed / assert_unlinkable. Both pipelines
            // visit them in source order, so ordinal pairing aligns
            // results across the two. A WACS "valid" against a wabt
            // "invalid" means the WAT parser produced an AST the
            // validator missed something on; the reverse means we're
            // over-rejecting.
            var script = TextScriptParser.ParseWast(File.ReadAllText(wastPath));
            var wacsResults = new List<(int Line, string Tag, string Result)>();
            foreach (var cmd in script)
            {
                switch (cmd)
                {
                    case ScriptModule sm:
                        wacsResults.Add((sm.Line, "module",
                            EvalScriptModule(sm)));
                        break;
                    case ScriptAssertInvalid ai:
                        wacsResults.Add((ai.Line, "assert_invalid",
                            EvalScriptModule(ai.Module)));
                        break;
                    case ScriptAssertMalformed am:
                        wacsResults.Add((am.Line, "assert_malformed",
                            EvalScriptModule(am.Module)));
                        break;
                    case ScriptAssertUnlinkable au:
                        wacsResults.Add((au.Line, "assert_unlinkable",
                            EvalScriptModule(au.Module)));
                        break;
                }
            }

            // ---- wabt baseline ----
            var wabtResults = new List<(int Line, string Tag, string Result)>();
            if (Directory.Exists(wabtJsonDir))
            {
                var jsonFiles = Directory.GetFiles(wabtJsonDir, "*.json");
                foreach (var jf in jsonFiles)
                {
                    var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jf));
                    foreach (var elem in doc.RootElement.GetProperty("commands").EnumerateArray())
                    {
                        var type = elem.GetProperty("type").GetString();
                        if (type != "module" && type != "module_definition"
                            && type != "assert_invalid" && type != "assert_malformed"
                            && type != "assert_unlinkable")
                            continue;
                        var line = elem.GetProperty("line").GetInt32();
                        if (!elem.TryGetProperty("filename", out var fnEl)) continue;
                        var wasmFile = Path.Combine(wabtJsonDir, fnEl.GetString()!);
                        wabtResults.Add((line, type, ResultOf(() =>
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
            // For divergence, we only care about (in)validity changing
            // direction, not the specific error message text.
            int n = Math.Min(wacsResults.Count, wabtResults.Count);
            for (int i = 0; i < n; i++)
            {
                if (Verdict(wacsResults[i].Result) != Verdict(wabtResults[i].Result))
                {
                    divs.Add(new Divergence
                    {
                        ModuleIndex = i,
                        Line = wacsResults[i].Line,
                        WacsResult = $"[{wacsResults[i].Tag}] {wacsResults[i].Result}",
                        WabtResult = $"[{wabtResults[i].Tag}] {wabtResults[i].Result}",
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

        /// <summary>
        /// Reduce a free-form result string ("valid" / "invalid: …" /
        /// "format-exception: …" / etc.) to a coarse verdict so the
        /// probe doesn't report false positives on differently-phrased
        /// equivalent errors.
        /// </summary>
        private static string Verdict(string raw)
        {
            if (raw == "valid") return "valid";
            return "rejected";
        }

        private static string EvalScriptModule(ScriptModule sm)
        {
            return ResultOf(() =>
            {
                Module module;
                if (sm.Module != null)
                {
                    module = sm.Module;
                }
                else if (sm.Kind == ScriptModuleKind.Binary && sm.Bytes != null)
                {
                    // Binary modules aren't eagerly parsed by
                    // TextScriptParser (Module stays null). Parse here
                    // so the probe can compare against wabt's
                    // already-binary input.
                    using var ms = new MemoryStream(sm.Bytes);
                    module = BinaryModuleParser.ParseWasm(ms);
                }
                else if (sm.Kind == ScriptModuleKind.Quote && sm.Bytes != null)
                {
                    // Quote modules: TextScriptParser eagerly parses
                    // them but swallows errors. Re-parse here to
                    // surface the error in the probe's verdict.
                    var text = System.Text.Encoding.UTF8.GetString(sm.Bytes);
                    module = TextModuleParser.ParseWat(text);
                }
                else
                {
                    return "rejected: parse failed";
                }
                var v = module.Validate();
                return v.IsValid ? "valid" : "invalid: " + Trunc(string.Join("|",
                    v.Errors.Take(2).Select(e => e.ErrorMessage)));
            });
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
