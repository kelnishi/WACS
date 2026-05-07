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
using System.Text;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Text;
using Wacs.Core.Types;

namespace Spec.Test
{
    /// <summary>
    /// Parallel-equivalence parsing test: for a given .wast file,
    /// parses every module two ways — through WACS's WAT parser, and
    /// through the wast2json-produced .wasm via WACS's binary parser —
    /// and reports structural divergences module-by-module.
    ///
    /// Catches the "WAT parser produces a valid-looking but
    /// structurally different Module than the binary parser" class
    /// of bug. The validator misses those (it accepts both), so they
    /// only surface at runtime or as missed assert_invalid checks.
    /// This diff catches them at parse time.
    ///
    /// Compares high-level fields (counts, types, imports, exports,
    /// initializer expression instruction shapes). Function-body
    /// instruction-by-instruction equivalence is intentionally
    /// out-of-scope — too noisy and the runtime equivalence test
    /// (existing JSON pipeline) covers it.
    /// </summary>
    public static class WastStructuralDiff
    {
        public sealed class ModuleDiff
        {
            public int ModuleIndex;
            public int Line;
            public List<string> Mismatches { get; } = new List<string>();
            public override string ToString() =>
                $"module #{ModuleIndex} (line {Line}): {Mismatches.Count} mismatch(es)";
        }

        public static IReadOnlyList<ModuleDiff> Compare(string wastPath, string wabtJsonDir)
        {
            BinaryModuleParser.ParseBranchHints = true;

            // ---- Collect WACS-parsed modules in source order ----
            var script = TextScriptParser.ParseWast(File.ReadAllText(wastPath));
            var wacsModules = new List<(int Line, Module? Mod)>();
            foreach (var cmd in script)
            {
                switch (cmd)
                {
                    case ScriptModule sm:
                        wacsModules.Add((sm.Line, MaterializeModule(sm)));
                        break;
                    case ScriptAssertInvalid ai:
                        wacsModules.Add((ai.Line, MaterializeModule(ai.Module)));
                        break;
                    case ScriptAssertMalformed am:
                        wacsModules.Add((am.Line, MaterializeModule(am.Module)));
                        break;
                    case ScriptAssertUnlinkable au:
                        wacsModules.Add((au.Line, MaterializeModule(au.Module)));
                        break;
                }
            }

            // ---- Collect wabt-binary-parsed modules in source order ----
            var wabtModules = new List<(int Line, Module? Mod)>();
            if (Directory.Exists(wabtJsonDir))
            {
                foreach (var jf in Directory.GetFiles(wabtJsonDir, "*.json"))
                {
                    var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jf));
                    foreach (var elem in doc.RootElement.GetProperty("commands").EnumerateArray())
                    {
                        var type = elem.GetProperty("type").GetString();
                        if (type != "module" && type != "module_definition"
                            && type != "assert_invalid" && type != "assert_malformed"
                            && type != "assert_unlinkable")
                            continue;
                        if (!elem.TryGetProperty("filename", out var fnEl)) continue;
                        var line = elem.GetProperty("line").GetInt32();
                        var wasmPath = Path.Combine(wabtJsonDir, fnEl.GetString()!);
                        Module? mod;
                        try
                        {
                            using var fs = File.OpenRead(wasmPath);
                            mod = BinaryModuleParser.ParseWasm(fs);
                        }
                        catch { mod = null; }
                        wabtModules.Add((line, mod));
                    }
                }
            }

            // ---- Pair ordinally and diff ----
            var diffs = new List<ModuleDiff>();
            int n = Math.Min(wacsModules.Count, wabtModules.Count);
            for (int i = 0; i < n; i++)
            {
                var d = new ModuleDiff { ModuleIndex = i, Line = wacsModules[i].Line };
                if (wacsModules[i].Mod == null && wabtModules[i].Mod == null) continue;
                if (wacsModules[i].Mod == null)
                {
                    d.Mismatches.Add("WACS parse failed; wabt parsed");
                    diffs.Add(d); continue;
                }
                if (wabtModules[i].Mod == null)
                {
                    d.Mismatches.Add("wabt parse failed; WACS parsed");
                    diffs.Add(d); continue;
                }
                CompareModules(wacsModules[i].Mod!, wabtModules[i].Mod!, d.Mismatches);
                if (d.Mismatches.Count > 0) diffs.Add(d);
            }
            return diffs;
        }

        // -----------------------------------------------------------
        // Module-level comparison
        // -----------------------------------------------------------

        private static void CompareModules(Module a, Module b, List<string> out_)
        {
            // Section counts. Mismatches here usually point to a parser
            // dropping (or duplicating) entries entirely.
            EqCount(out_, "Types", a.Types.Count, b.Types.Count);
            EqCount(out_, "Funcs", a.Funcs.Count, b.Funcs.Count);
            EqCount(out_, "Tables", a.Tables.Count, b.Tables.Count);
            EqCount(out_, "Memories", a.Memories.Count, b.Memories.Count);
            EqCount(out_, "Globals", a.Globals.Count, b.Globals.Count);
            EqCount(out_, "Elements", a.Elements.Length, b.Elements.Length);
            EqCount(out_, "Datas", a.Datas.Length, b.Datas.Length);
            EqCount(out_, "Exports", a.Exports.Length, b.Exports.Length);
            EqCount(out_, "Imports", a.Imports.Length, b.Imports.Length);
            EqCount(out_, "Tags", a.Tags.Count, b.Tags.Count);

            // Per-section type compare. Skip if counts already differ.
            int n;

            n = Math.Min(a.Funcs.Count, b.Funcs.Count);
            for (int i = 0; i < n; i++)
            {
                if ((int)a.Funcs[i].TypeIndex.Value != (int)b.Funcs[i].TypeIndex.Value)
                    out_.Add($"Funcs[{i}].TypeIndex: WACS={a.Funcs[i].TypeIndex.Value} wabt={b.Funcs[i].TypeIndex.Value}");
                if (a.Funcs[i].Locals.Length != b.Funcs[i].Locals.Length)
                    out_.Add($"Funcs[{i}].Locals.Length: WACS={a.Funcs[i].Locals.Length} wabt={b.Funcs[i].Locals.Length}");
                CompareInitExpr(out_, $"Funcs[{i}].Body", a.Funcs[i].Body, b.Funcs[i].Body);
            }

            n = Math.Min(a.Globals.Count, b.Globals.Count);
            for (int i = 0; i < n; i++)
            {
                if (a.Globals[i].Type.ContentType != b.Globals[i].Type.ContentType)
                    out_.Add($"Globals[{i}].Type: WACS={a.Globals[i].Type.ContentType} wabt={b.Globals[i].Type.ContentType}");
                if (a.Globals[i].Type.Mutability != b.Globals[i].Type.Mutability)
                    out_.Add($"Globals[{i}].Mut: WACS={a.Globals[i].Type.Mutability} wabt={b.Globals[i].Type.Mutability}");
                CompareInitExpr(out_, $"Globals[{i}].Init", a.Globals[i].Initializer, b.Globals[i].Initializer);
            }

            n = Math.Min(a.Tables.Count, b.Tables.Count);
            for (int i = 0; i < n; i++)
            {
                if (a.Tables[i].ElementType != b.Tables[i].ElementType)
                    out_.Add($"Tables[{i}].ElementType: WACS={a.Tables[i].ElementType} wabt={b.Tables[i].ElementType}");
                if (a.Tables[i].Limits.Minimum != b.Tables[i].Limits.Minimum)
                    out_.Add($"Tables[{i}].Min: WACS={a.Tables[i].Limits.Minimum} wabt={b.Tables[i].Limits.Minimum}");
                if (a.Tables[i].Limits.Maximum != b.Tables[i].Limits.Maximum)
                    out_.Add($"Tables[{i}].Max: WACS={a.Tables[i].Limits.Maximum} wabt={b.Tables[i].Limits.Maximum}");
                if (a.Tables[i].Limits.AddressType != b.Tables[i].Limits.AddressType)
                    out_.Add($"Tables[{i}].AddrType: WACS={a.Tables[i].Limits.AddressType} wabt={b.Tables[i].Limits.AddressType}");
            }

            n = Math.Min(a.Memories.Count, b.Memories.Count);
            for (int i = 0; i < n; i++)
            {
                if (a.Memories[i].Limits.Minimum != b.Memories[i].Limits.Minimum)
                    out_.Add($"Memories[{i}].Min: WACS={a.Memories[i].Limits.Minimum} wabt={b.Memories[i].Limits.Minimum}");
                if (a.Memories[i].Limits.Maximum != b.Memories[i].Limits.Maximum)
                    out_.Add($"Memories[{i}].Max: WACS={a.Memories[i].Limits.Maximum} wabt={b.Memories[i].Limits.Maximum}");
                if (a.Memories[i].Limits.AddressType != b.Memories[i].Limits.AddressType)
                    out_.Add($"Memories[{i}].AddrType: WACS={a.Memories[i].Limits.AddressType} wabt={b.Memories[i].Limits.AddressType}");
            }

            n = Math.Min(a.Elements.Length, b.Elements.Length);
            for (int i = 0; i < n; i++)
            {
                CompareElement(out_, i, a.Elements[i], b.Elements[i]);
            }

            n = Math.Min(a.Datas.Length, b.Datas.Length);
            for (int i = 0; i < n; i++)
            {
                CompareData(out_, i, a.Datas[i], b.Datas[i]);
            }

            n = Math.Min(a.Exports.Length, b.Exports.Length);
            for (int i = 0; i < n; i++)
            {
                if (a.Exports[i].Name != b.Exports[i].Name)
                    out_.Add($"Exports[{i}].Name: WACS={a.Exports[i].Name} wabt={b.Exports[i].Name}");
                if (a.Exports[i].Desc.GetType() != b.Exports[i].Desc.GetType())
                {
                    out_.Add($"Exports[{i}].Kind: WACS={a.Exports[i].Desc.GetType().Name} wabt={b.Exports[i].Desc.GetType().Name}");
                    continue;
                }
                // Also compare the target index (which func / table /
                // memory / global the export points at).
                var rA = a.Exports[i].Desc.ToString();
                var rB = b.Exports[i].Desc.ToString();
                if (rA != rB)
                    out_.Add($"Exports[{i}].Desc: WACS={rA} wabt={rB}");
            }

            n = Math.Min(a.Imports.Length, b.Imports.Length);
            for (int i = 0; i < n; i++)
            {
                if (a.Imports[i].ModuleName != b.Imports[i].ModuleName)
                    out_.Add($"Imports[{i}].ModuleName: WACS={a.Imports[i].ModuleName} wabt={b.Imports[i].ModuleName}");
                if (a.Imports[i].Name != b.Imports[i].Name)
                    out_.Add($"Imports[{i}].Name: WACS={a.Imports[i].Name} wabt={b.Imports[i].Name}");
                if (a.Imports[i].Desc.GetType() != b.Imports[i].Desc.GetType())
                {
                    out_.Add($"Imports[{i}].Kind: WACS={a.Imports[i].Desc.GetType().Name} wabt={b.Imports[i].Desc.GetType().Name}");
                    continue;
                }
                var rA = a.Imports[i].Desc.ToString();
                var rB = b.Imports[i].Desc.ToString();
                if (rA != rB)
                    out_.Add($"Imports[{i}].Desc: WACS={rA} wabt={rB}");
            }

            // StartIndex
            if (a.StartIndex.Value != b.StartIndex.Value)
                out_.Add($"StartIndex: WACS={a.StartIndex.Value} wabt={b.StartIndex.Value}");
        }

        private static void CompareElement(List<string> out_, int idx, Module.ElementSegment a, Module.ElementSegment b)
        {
            if (a.Type != b.Type)
                out_.Add($"Elements[{idx}].Type: WACS={a.Type} wabt={b.Type}");
            if (a.Mode.GetType() != b.Mode.GetType())
                out_.Add($"Elements[{idx}].Mode: WACS={a.Mode.GetType().Name} wabt={b.Mode.GetType().Name}");
            if (a.Initializers.Length != b.Initializers.Length)
                out_.Add($"Elements[{idx}].Inits.Length: WACS={a.Initializers.Length} wabt={b.Initializers.Length}");
            int n = Math.Min(a.Initializers.Length, b.Initializers.Length);
            for (int i = 0; i < n; i++)
                CompareInitExpr(out_, $"Elements[{idx}].Inits[{i}]", a.Initializers[i], b.Initializers[i]);
            // Active mode: also compare offset expression and table idx.
            if (a.Mode is Module.ElementMode.ActiveMode aa && b.Mode is Module.ElementMode.ActiveMode bb)
            {
                if (aa.TableIndex.Value != bb.TableIndex.Value)
                    out_.Add($"Elements[{idx}].TableIdx: WACS={aa.TableIndex.Value} wabt={bb.TableIndex.Value}");
                CompareInitExpr(out_, $"Elements[{idx}].Offset", aa.Offset, bb.Offset);
            }
        }

        private static void CompareData(List<string> out_, int idx, Module.Data a, Module.Data b)
        {
            if (a.Mode.GetType() != b.Mode.GetType())
                out_.Add($"Datas[{idx}].Mode: WACS={a.Mode.GetType().Name} wabt={b.Mode.GetType().Name}");
            if (a.Init.Length != b.Init.Length)
                out_.Add($"Datas[{idx}].Init.Length: WACS={a.Init.Length} wabt={b.Init.Length}");
            if (a.Mode is Module.DataMode.ActiveMode aa && b.Mode is Module.DataMode.ActiveMode bb)
            {
                if (aa.MemoryIndex.Value != bb.MemoryIndex.Value)
                    out_.Add($"Datas[{idx}].MemIdx: WACS={aa.MemoryIndex.Value} wabt={bb.MemoryIndex.Value}");
                CompareInitExpr(out_, $"Datas[{idx}].Offset", aa.Offset, bb.Offset);
            }
        }

        private static void CompareInitExpr(List<string> out_, string label, Expression a, Expression b)
        {
            // Compare the instruction-shape sequence, ignoring trailing
            // End markers (binary always has one; WAT's may or may not).
            var instsA = a.Instructions.Where(x => !(x is InstEnd)).ToList();
            var instsB = b.Instructions.Where(x => !(x is InstEnd)).ToList();
            if (instsA.Count != instsB.Count)
            {
                out_.Add($"{label}: instr-count WACS={instsA.Count} wabt={instsB.Count} " +
                         $"(WACS={Render(instsA)} wabt={Render(instsB)})");
                return;
            }
            for (int i = 0; i < instsA.Count; i++)
            {
                if (instsA[i].GetType() != instsB[i].GetType())
                    out_.Add($"{label}[{i}]: WACS={instsA[i].GetType().Name} wabt={instsB[i].GetType().Name}");
                // Operand-level checks for the few common instruction
                // types where field divergence has practical impact
                // (function references, locals, type indices). String
                // comparison via RenderText avoids per-type plumbing.
                else
                {
                    var rA = TryRender(instsA[i]);
                    var rB = TryRender(instsB[i]);
                    if (rA != rB)
                        out_.Add($"{label}[{i}].text: WACS={rA} wabt={rB}");
                }
            }
        }

        private static string TryRender(InstructionBase inst)
        {
            try { return inst.RenderText(null); }
            catch { return inst.GetType().Name; }
        }

        private static string Render(List<InstructionBase> insts) =>
            "[" + string.Join(", ", insts.Select(i => i.GetType().Name)) + "]";

        private static void EqCount(List<string> out_, string field, int a, int b)
        {
            if (a != b) out_.Add($"{field}.Count: WACS={a} wabt={b}");
        }

        private static Module? MaterializeModule(ScriptModule sm)
        {
            try
            {
                if (sm.Module != null) return sm.Module;
                if (sm.Kind == ScriptModuleKind.Binary && sm.Bytes != null)
                {
                    using var ms = new MemoryStream(sm.Bytes);
                    return BinaryModuleParser.ParseWasm(ms);
                }
                if (sm.Kind == ScriptModuleKind.Quote && sm.Bytes != null)
                {
                    return TextModuleParser.ParseWat(Encoding.UTF8.GetString(sm.Bytes));
                }
                return null;
            }
            catch { return null; }
        }
    }
}
