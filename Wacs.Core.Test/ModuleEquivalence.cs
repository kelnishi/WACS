// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Types;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Structural comparison of two <see cref="Module"/> objects. Used by
    /// the spec-suite equivalence test to verify that the new text parser
    /// produces Modules structurally-identical to what
    /// <see cref="BinaryModuleParser.ParseWasm"/> produces on the same
    /// source.
    ///
    /// <para>This is a diagnostic tool, not a full deep-equals. Reports the
    /// first set of mismatches so a failing test is actionable.</para>
    /// </summary>
    internal static class ModuleEquivalence
    {
        public sealed class Report
        {
            public readonly List<string> Mismatches = new List<string>();
            public bool IsMatch => Mismatches.Count == 0;

            public override string ToString()
            {
                if (IsMatch) return "match";
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{Mismatches.Count} mismatch(es):");
                for (int i = 0; i < System.Math.Min(10, Mismatches.Count); i++)
                    sb.AppendLine("  - " + Mismatches[i]);
                if (Mismatches.Count > 10)
                    sb.AppendLine($"  (+ {Mismatches.Count - 10} more)");
                return sb.ToString();
            }
        }

        public static Report Compare(Module a, Module b)
        {
            var r = new Report();
            CompareTypes(a, b, r);
            CompareImports(a, b, r);
            CompareFuncs(a, b, r);
            CompareTables(a, b, r);
            CompareMemories(a, b, r);
            CompareGlobals(a, b, r);
            CompareExports(a, b, r);
            CompareStart(a, b, r);
            CompareElements(a, b, r);
            CompareDatas(a, b, r);
            return r;
        }

        private static void CompareTypes(Module a, Module b, Report r)
        {
            if (a.Types.Count != b.Types.Count)
            {
                r.Mismatches.Add($"Types.Count: {a.Types.Count} vs {b.Types.Count}");
                return;
            }
            for (int i = 0; i < a.Types.Count; i++)
            {
                var fa = a.Types[i].SubTypes[0].Body as FunctionType;
                var fb = b.Types[i].SubTypes[0].Body as FunctionType;
                if (fa == null || fb == null)
                {
                    if ((fa == null) != (fb == null))
                        r.Mismatches.Add($"Types[{i}] body kind differs (GC? vs func)");
                    continue;
                }
                CompareResultType($"Types[{i}].Params", fa.ParameterTypes, fb.ParameterTypes, r);
                CompareResultType($"Types[{i}].Results", fa.ResultType, fb.ResultType, r);
            }
        }

        private static void CompareResultType(string path, ResultType a, ResultType b, Report r)
        {
            if (a.Arity != b.Arity)
            {
                r.Mismatches.Add($"{path}.Arity: {a.Arity} vs {b.Arity}");
                return;
            }
            for (int i = 0; i < a.Arity; i++)
                if (a.Types[i] != b.Types[i])
                    r.Mismatches.Add($"{path}[{i}]: {a.Types[i]} vs {b.Types[i]}");
        }

        private static void CompareImports(Module a, Module b, Report r)
        {
            if (a.Imports.Length != b.Imports.Length)
            {
                r.Mismatches.Add($"Imports.Length: {a.Imports.Length} vs {b.Imports.Length}");
                return;
            }
            for (int i = 0; i < a.Imports.Length; i++)
            {
                var x = a.Imports[i];
                var y = b.Imports[i];
                if (x.ModuleName != y.ModuleName)
                    r.Mismatches.Add($"Imports[{i}].ModuleName: '{x.ModuleName}' vs '{y.ModuleName}'");
                if (x.Name != y.Name)
                    r.Mismatches.Add($"Imports[{i}].Name: '{x.Name}' vs '{y.Name}'");
                if (x.Desc.GetType() != y.Desc.GetType())
                {
                    r.Mismatches.Add($"Imports[{i}].Desc kind: {x.Desc.GetType().Name} vs {y.Desc.GetType().Name}");
                    continue;
                }
                switch (x.Desc)
                {
                    case Module.ImportDesc.FuncDesc fd:
                    {
                        var gd = (Module.ImportDesc.FuncDesc)y.Desc;
                        if (fd.TypeIndex.Value != gd.TypeIndex.Value)
                            r.Mismatches.Add($"Imports[{i}].FuncDesc.TypeIndex: {fd.TypeIndex.Value} vs {gd.TypeIndex.Value}");
                        break;
                    }
                    case Module.ImportDesc.TableDesc td:
                    {
                        var gd = (Module.ImportDesc.TableDesc)y.Desc;
                        CompareLimits($"Imports[{i}].TableDesc.Limits", td.TableDef.Limits, gd.TableDef.Limits, r);
                        if (td.TableDef.ElementType != gd.TableDef.ElementType)
                            r.Mismatches.Add($"Imports[{i}].TableDesc.ElementType: {td.TableDef.ElementType} vs {gd.TableDef.ElementType}");
                        break;
                    }
                    case Module.ImportDesc.MemDesc md:
                    {
                        var gd = (Module.ImportDesc.MemDesc)y.Desc;
                        CompareLimits($"Imports[{i}].MemDesc.Limits", md.MemDef.Limits, gd.MemDef.Limits, r);
                        break;
                    }
                    case Module.ImportDesc.GlobalDesc gd:
                    {
                        var hd = (Module.ImportDesc.GlobalDesc)y.Desc;
                        if (gd.GlobalDef.ContentType != hd.GlobalDef.ContentType)
                            r.Mismatches.Add($"Imports[{i}].GlobalDesc.ContentType: {gd.GlobalDef.ContentType} vs {hd.GlobalDef.ContentType}");
                        if (gd.GlobalDef.Mutability != hd.GlobalDef.Mutability)
                            r.Mismatches.Add($"Imports[{i}].GlobalDesc.Mutability: {gd.GlobalDef.Mutability} vs {hd.GlobalDef.Mutability}");
                        break;
                    }
                    case Module.ImportDesc.TagDesc td:
                    {
                        var gd = (Module.ImportDesc.TagDesc)y.Desc;
                        if (td.TagDef.TypeIndex.Value != gd.TagDef.TypeIndex.Value)
                            r.Mismatches.Add($"Imports[{i}].TagDesc.TypeIndex: {td.TagDef.TypeIndex.Value} vs {gd.TagDef.TypeIndex.Value}");
                        break;
                    }
                }
            }
        }

        private static void CompareLimits(string path, Limits a, Limits b, Report r)
        {
            if (a.AddressType != b.AddressType)
                r.Mismatches.Add($"{path}.AddressType: {a.AddressType} vs {b.AddressType}");
            if (a.Minimum != b.Minimum)
                r.Mismatches.Add($"{path}.Minimum: {a.Minimum} vs {b.Minimum}");
            if (a.Maximum != b.Maximum)
                r.Mismatches.Add($"{path}.Maximum: {a.Maximum?.ToString() ?? "?"} vs {b.Maximum?.ToString() ?? "?"}");
            if (a.Shared != b.Shared)
                r.Mismatches.Add($"{path}.Shared: {a.Shared} vs {b.Shared}");
        }

        private static void CompareFuncs(Module a, Module b, Report r)
        {
            if (a.Funcs.Count != b.Funcs.Count)
            {
                r.Mismatches.Add($"Funcs.Count: {a.Funcs.Count} vs {b.Funcs.Count}");
                return;
            }
            for (int i = 0; i < a.Funcs.Count; i++)
            {
                if (a.Funcs[i].TypeIndex.Value != b.Funcs[i].TypeIndex.Value)
                    r.Mismatches.Add($"Funcs[{i}].TypeIndex: {a.Funcs[i].TypeIndex.Value} vs {b.Funcs[i].TypeIndex.Value}");
                var la = a.Funcs[i].Locals ?? System.Array.Empty<Wacs.Core.Types.Defs.ValType>();
                var lb = b.Funcs[i].Locals ?? System.Array.Empty<Wacs.Core.Types.Defs.ValType>();
                if (la.Length != lb.Length)
                {
                    r.Mismatches.Add($"Funcs[{i}].Locals.Length: {la.Length} vs {lb.Length}");
                    continue;
                }
                for (int k = 0; k < la.Length; k++)
                    if (la[k] != lb[k])
                        r.Mismatches.Add($"Funcs[{i}].Locals[{k}]: {la[k]} vs {lb[k]}");

                // Body — compare top-level opcode sequence. This is a coarse
                // signal that catches missing ops, but not all semantic
                // differences (immediate values not yet cross-checked).
                CompareInstructionSeq($"Funcs[{i}].Body", a.Funcs[i].Body.Instructions, b.Funcs[i].Body.Instructions, r);
            }
        }

        private static void CompareInstructionSeq(string path, InstructionSequence a, InstructionSequence b, Report r)
        {
            if (a.Count != b.Count)
            {
                r.Mismatches.Add($"{path}.Count: {a.Count} vs {b.Count}");
                return;
            }
            for (int i = 0; i < a.Count; i++)
            {
                var ia = a[i]!;
                var ib = b[i]!;
                if (!ia.Op.Equals(ib.Op))
                {
                    r.Mismatches.Add($"{path}[{i}].Op: {ia.Op} vs {ib.Op}");
                }
                // Recurse into block bodies.
                if (ia is IBlockInstruction ba && ib is IBlockInstruction bb)
                {
                    if (ba.Count != bb.Count)
                    {
                        r.Mismatches.Add($"{path}[{i}] block count: {ba.Count} vs {bb.Count}");
                    }
                    int nested = System.Math.Min(ba.Count, bb.Count);
                    for (int k = 0; k < nested; k++)
                        CompareInstructionSeq($"{path}[{i}].Block[{k}]",
                            ba.GetBlock(k).Instructions, bb.GetBlock(k).Instructions, r);
                }
            }
        }

        private static void CompareTables(Module a, Module b, Report r)
        {
            if (a.Tables.Count != b.Tables.Count)
            {
                r.Mismatches.Add($"Tables.Count: {a.Tables.Count} vs {b.Tables.Count}");
                return;
            }
            for (int i = 0; i < a.Tables.Count; i++)
            {
                if (a.Tables[i].ElementType != b.Tables[i].ElementType)
                    r.Mismatches.Add($"Tables[{i}].ElementType: {a.Tables[i].ElementType} vs {b.Tables[i].ElementType}");
                CompareLimits($"Tables[{i}].Limits", a.Tables[i].Limits, b.Tables[i].Limits, r);
            }
        }

        private static void CompareMemories(Module a, Module b, Report r)
        {
            if (a.Memories.Count != b.Memories.Count)
            {
                r.Mismatches.Add($"Memories.Count: {a.Memories.Count} vs {b.Memories.Count}");
                return;
            }
            for (int i = 0; i < a.Memories.Count; i++)
                CompareLimits($"Memories[{i}].Limits", a.Memories[i].Limits, b.Memories[i].Limits, r);
        }

        private static void CompareGlobals(Module a, Module b, Report r)
        {
            if (a.Globals.Count != b.Globals.Count)
            {
                r.Mismatches.Add($"Globals.Count: {a.Globals.Count} vs {b.Globals.Count}");
                return;
            }
            for (int i = 0; i < a.Globals.Count; i++)
            {
                if (a.Globals[i].Type.ContentType != b.Globals[i].Type.ContentType)
                    r.Mismatches.Add($"Globals[{i}].ContentType: {a.Globals[i].Type.ContentType} vs {b.Globals[i].Type.ContentType}");
                if (a.Globals[i].Type.Mutability != b.Globals[i].Type.Mutability)
                    r.Mismatches.Add($"Globals[{i}].Mutability: {a.Globals[i].Type.Mutability} vs {b.Globals[i].Type.Mutability}");
            }
        }

        private static void CompareExports(Module a, Module b, Report r)
        {
            if (a.Exports.Length != b.Exports.Length)
            {
                r.Mismatches.Add($"Exports.Length: {a.Exports.Length} vs {b.Exports.Length}");
                return;
            }
            for (int i = 0; i < a.Exports.Length; i++)
            {
                if (a.Exports[i].Name != b.Exports[i].Name)
                    r.Mismatches.Add($"Exports[{i}].Name: '{a.Exports[i].Name}' vs '{b.Exports[i].Name}'");
                if (a.Exports[i].Desc.GetType() != b.Exports[i].Desc.GetType())
                    r.Mismatches.Add($"Exports[{i}].Desc kind: {a.Exports[i].Desc.GetType().Name} vs {b.Exports[i].Desc.GetType().Name}");
            }
        }

        private static void CompareStart(Module a, Module b, Report r)
        {
            if (a.StartIndex.Value != b.StartIndex.Value)
                r.Mismatches.Add($"StartIndex: {a.StartIndex.Value} vs {b.StartIndex.Value}");
        }

        private static void CompareElements(Module a, Module b, Report r)
        {
            if (a.Elements.Length != b.Elements.Length)
                r.Mismatches.Add($"Elements.Length: {a.Elements.Length} vs {b.Elements.Length}");
        }

        private static void CompareDatas(Module a, Module b, Report r)
        {
            if (a.Datas.Length != b.Datas.Length)
                r.Mismatches.Add($"Datas.Length: {a.Datas.Length} vs {b.Datas.Length}");
        }
    }
}
