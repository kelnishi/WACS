// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wacs.Core.Bin;
using Wacs.Core.Text;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Convert a parsed .wast script into the `wast2json` output shape:
    /// a JSON file listing commands plus side-car .wasm files for every
    /// module referenced. Mirrors what `wabt`'s <c>wast2json</c> emits
    /// so the resulting bundle is consumable by any tooling that reads
    /// the spec-test format (notably <c>Spec.Test</c> in this repo).
    /// </summary>
    public static class WastJsonEmitter
    {
        public sealed class Output
        {
            public string JsonPath = "";
            public List<string> WasmPaths { get; } = new();
        }

        /// <summary>
        /// Emit JSON + side-car .wasm files into <paramref name="outputDir"/>.
        /// <paramref name="baseName"/> is the stem for all generated files
        /// (e.g. "i32" yields "i32.json" + "i32.0.wasm" / "i32.1.wasm" …).
        /// <paramref name="sourceFilename"/> populates the JSON's
        /// <c>source_filename</c> field for traceability — typically the
        /// original <c>.wast</c> path's leaf name.
        /// </summary>
        public static Output Emit(
            IReadOnlyList<ScriptCommand> commands,
            string outputDir,
            string baseName,
            string sourceFilename)
        {
            Directory.CreateDirectory(outputDir);
            var result = new Output();
            var jsonCommands = new JsonArray();
            int moduleIndex = 0;

            foreach (var cmd in commands)
            {
                switch (cmd)
                {
                    case ScriptModule sm:
                        jsonCommands.Add(EmitModuleCommand(
                            sm, outputDir, baseName, ref moduleIndex, result));
                        break;
                    case ScriptRegister r:
                        jsonCommands.Add(EmitRegister(r));
                        break;
                    case ScriptInvoke inv:
                        jsonCommands.Add(EmitBareAction("action", inv));
                        break;
                    case ScriptGet get:
                        jsonCommands.Add(EmitBareAction("action", get));
                        break;
                    case ScriptAssertReturn ar:
                        jsonCommands.Add(EmitAssertReturn(ar));
                        break;
                    case ScriptAssertTrap at:
                        jsonCommands.Add(EmitAssertTrap(
                            at, outputDir, baseName, ref moduleIndex, result));
                        break;
                    case ScriptAssertExhaustion ae:
                        jsonCommands.Add(EmitAssertExhaustion(ae));
                        break;
                    case ScriptAssertInvalid ai:
                        jsonCommands.Add(EmitAssertModuleFailure(
                            "assert_invalid", ai.Line, ai.Module, ai.ExpectedMessage,
                            outputDir, baseName, ref moduleIndex, result));
                        break;
                    case ScriptAssertMalformed am:
                        jsonCommands.Add(EmitAssertModuleFailure(
                            "assert_malformed", am.Line, am.Module, am.ExpectedMessage,
                            outputDir, baseName, ref moduleIndex, result));
                        break;
                    case ScriptAssertUnlinkable au:
                        jsonCommands.Add(EmitAssertModuleFailure(
                            "assert_unlinkable", au.Line, au.Module, au.ExpectedMessage,
                            outputDir, baseName, ref moduleIndex, result));
                        break;
                    case ScriptAssertException ax:
                        jsonCommands.Add(EmitAssertException(ax));
                        break;
                    default:
                        // Unknown command kind — skip, but record it
                        // structurally so the runner sees it.
                        jsonCommands.Add(new JsonObject
                        {
                            ["type"] = "unknown",
                            ["line"] = cmd.Line,
                        });
                        break;
                }
            }

            var root = new JsonObject
            {
                ["source_filename"] = sourceFilename,
                ["commands"] = jsonCommands,
            };

            result.JsonPath = Path.Combine(outputDir, baseName + ".json");
            File.WriteAllText(result.JsonPath, root.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }

        // ---- Module-bearing commands ----------------------------------

        private static JsonObject EmitModuleCommand(
            ScriptModule sm, string outputDir, string baseName,
            ref int moduleIndex, Output result)
        {
            var obj = new JsonObject
            {
                ["type"] = "module",
                ["line"] = sm.Line,
            };
            // (module instance $alias $src) is a re-binding, not a fresh
            // module — emit a module-instance record without a filename.
            // The spec runner resolves $src and re-instantiates.
            if (sm.Kind == ScriptModuleKind.Instance)
            {
                obj["type"] = "module_instance";
                if (sm.Id != null) obj["instance"] = sm.Id;
                return obj;
            }
            string fname = MaterializeModule(
                sm, outputDir, baseName, ref moduleIndex, result);
            obj["filename"] = fname;
            if (sm.Id != null) obj["name"] = sm.Id;
            return obj;
        }

        private static JsonObject EmitAssertModuleFailure(
            string type, int line, ScriptModule sm, string expectedText,
            string outputDir, string baseName,
            ref int moduleIndex, Output result)
        {
            var obj = new JsonObject
            {
                ["type"] = type,
                ["line"] = line,
            };
            string fname = MaterializeModule(
                sm, outputDir, baseName, ref moduleIndex, result);
            obj["filename"] = fname;
            obj["text"] = expectedText;
            obj["module_type"] = sm.Kind == ScriptModuleKind.Quote
                || sm.Module == null
                ? "text"
                : "binary";
            return obj;
        }

        /// <summary>
        /// Write the side-car file for <paramref name="sm"/> and return
        /// its filename (relative to <paramref name="outputDir"/>).
        /// Three module kinds collapse to two file types:
        /// <list type="bullet">
        ///   <item>Text → serialize via <see cref="BinaryModuleWriter"/>
        ///     into a <c>.wasm</c>.</item>
        ///   <item>Binary → write the captured bytes verbatim as
        ///     <c>.wasm</c>.</item>
        ///   <item>Quote → write the quoted text as <c>.wat</c> (the
        ///     assertion expects parse failure; the runner will reparse
        ///     the text).</item>
        /// </list>
        /// </summary>
        private static string MaterializeModule(
            ScriptModule sm, string outputDir, string baseName,
            ref int moduleIndex, Output result)
        {
            int idx = moduleIndex++;
            string fname;
            byte[]? data = null;

            switch (sm.Kind)
            {
                case ScriptModuleKind.Binary:
                    fname = $"{baseName}.{idx}.wasm";
                    data = sm.Bytes ?? Array.Empty<byte>();
                    break;
                case ScriptModuleKind.Quote:
                    fname = $"{baseName}.{idx}.wat";
                    data = sm.Bytes ?? Array.Empty<byte>();
                    break;
                case ScriptModuleKind.Text:
                default:
                    fname = $"{baseName}.{idx}.wasm";
                    if (sm.Module != null)
                    {
                        try { data = BinaryModuleWriter.Write(sm.Module); }
                        catch { data = null; }
                    }
                    // Parse-failed text module (inside an
                    // assert_malformed / assert_invalid) — render the
                    // canonical re-parse text or a stub so the file
                    // exists. The assertion will be satisfied by the
                    // runner re-parsing this text anyway.
                    if (data == null)
                    {
                        fname = $"{baseName}.{idx}.wat";
                        data = Encoding.UTF8.GetBytes(
                            $"(; module parse failed at script line {sm.Line} ;)\n");
                    }
                    break;
            }

            string fullPath = Path.Combine(outputDir, fname);
            File.WriteAllBytes(fullPath, data);
            result.WasmPaths.Add(fullPath);
            return fname;
        }

        // ---- Non-module commands --------------------------------------

        private static JsonObject EmitRegister(ScriptRegister r)
        {
            var obj = new JsonObject
            {
                ["type"] = "register",
                ["line"] = r.Line,
                ["as"] = r.ExportName,
            };
            if (r.ModuleId != null) obj["name"] = r.ModuleId;
            return obj;
        }

        private static JsonObject EmitBareAction(string outerType, ScriptAction action)
        {
            return new JsonObject
            {
                ["type"] = outerType,
                ["line"] = action.Line,
                ["action"] = EmitAction(action),
            };
        }

        private static JsonObject EmitAssertReturn(ScriptAssertReturn ar)
        {
            var obj = new JsonObject
            {
                ["type"] = "assert_return",
                ["line"] = ar.Line,
                ["action"] = EmitAction(ar.Action),
                ["expected"] = EmitValueList(ar.Expected),
            };
            return obj;
        }

        private static JsonObject EmitAssertTrap(
            ScriptAssertTrap at, string outputDir, string baseName,
            ref int moduleIndex, Output result)
        {
            // Two shapes: action-traps and module-traps (start-trap).
            // The runner discriminates by presence of `action` vs
            // `filename`, matching wast2json's emission.
            if (at.Action != null)
            {
                return new JsonObject
                {
                    ["type"] = "assert_trap",
                    ["line"] = at.Line,
                    ["action"] = EmitAction(at.Action),
                    ["text"] = at.ExpectedMessage,
                    ["expected"] = new JsonArray(),
                };
            }
            // (assert_trap (module …) "msg") — a start-function trap.
            // wast2json emits this as "assert_uninstantiable".
            string fname = MaterializeModule(
                at.Module ?? throw new InvalidOperationException(
                    "assert_trap missing both action and module"),
                outputDir, baseName, ref moduleIndex, result);
            return new JsonObject
            {
                ["type"] = "assert_uninstantiable",
                ["line"] = at.Line,
                ["filename"] = fname,
                ["text"] = at.ExpectedMessage,
                ["module_type"] = "binary",
            };
        }

        private static JsonObject EmitAssertExhaustion(ScriptAssertExhaustion ae)
        {
            return new JsonObject
            {
                ["type"] = "assert_exhaustion",
                ["line"] = ae.Line,
                ["action"] = EmitAction(ae.Action),
                ["text"] = ae.ExpectedMessage,
                ["expected"] = new JsonArray(),
            };
        }

        private static JsonObject EmitAssertException(ScriptAssertException ax)
        {
            return new JsonObject
            {
                ["type"] = "assert_exception",
                ["line"] = ax.Line,
                ["action"] = EmitAction(ax.Action),
            };
        }

        // ---- Actions --------------------------------------------------

        private static JsonObject EmitAction(ScriptAction action)
        {
            switch (action)
            {
                case ScriptInvoke inv:
                {
                    var obj = new JsonObject
                    {
                        ["type"] = "invoke",
                        ["field"] = inv.ExportName,
                    };
                    if (inv.ModuleId != null) obj["module"] = inv.ModuleId;
                    obj["args"] = EmitValueList(inv.Args);
                    return obj;
                }
                case ScriptGet g:
                {
                    var obj = new JsonObject
                    {
                        ["type"] = "get",
                        ["field"] = g.ExportName,
                    };
                    if (g.ModuleId != null) obj["module"] = g.ModuleId;
                    return obj;
                }
                default:
                    throw new InvalidOperationException(
                        $"unknown action kind {action.GetType().Name}");
            }
        }

        // ---- Values ---------------------------------------------------

        private static JsonArray EmitValueList(IEnumerable<ScriptValue> values)
        {
            var arr = new JsonArray();
            foreach (var v in values) arr.Add(EmitValue(v));
            return arr;
        }

        /// <summary>
        /// Render a <see cref="ScriptValue"/> in wast2json's
        /// <c>{"type": "...", "value": "..."}</c> shape. Floats are
        /// emitted as their unsigned bit-pattern decimal — what
        /// wast2json does — so the runner reconstructs the exact
        /// IEEE-754 bits including NaN payloads. <c>nan:canonical</c>
        /// and <c>nan:arithmetic</c> ride along as string sentinels.
        /// </summary>
        private static JsonObject EmitValue(ScriptValue v)
        {
            switch (v.Kind)
            {
                case ScriptValueKind.I32:
                    return new JsonObject
                    {
                        ["type"] = "i32",
                        ["value"] = unchecked((uint)v.I32).ToString(CultureInfo.InvariantCulture),
                    };
                case ScriptValueKind.I64:
                    return new JsonObject
                    {
                        ["type"] = "i64",
                        ["value"] = unchecked((ulong)v.I64).ToString(CultureInfo.InvariantCulture),
                    };
                case ScriptValueKind.F32:
                    return new JsonObject
                    {
                        ["type"] = "f32",
                        ["value"] = FloatPatternToString(v.FloatPattern)
                            ?? v.F32Bits.ToString(CultureInfo.InvariantCulture),
                    };
                case ScriptValueKind.F64:
                    return new JsonObject
                    {
                        ["type"] = "f64",
                        ["value"] = FloatPatternToString(v.FloatPattern)
                            ?? v.F64Bits.ToString(CultureInfo.InvariantCulture),
                    };
                case ScriptValueKind.V128:
                {
                    var lanes = new JsonArray();
                    if (v.V128Lanes != null)
                        foreach (var lane in v.V128Lanes) lanes.Add(lane);
                    return new JsonObject
                    {
                        ["type"] = "v128",
                        ["lane_type"] = v.V128LaneType ?? "i8",
                        ["value"] = lanes,
                    };
                }
                case ScriptValueKind.RefNull:
                    // wast2json picks the typed null based on the heap
                    // type: ref.null func → funcref/null, ref.null
                    // extern → externref/null, etc.
                    return new JsonObject
                    {
                        ["type"] = NullTypeForHeap(v.RefHeapType),
                        ["value"] = "null",
                    };
                case ScriptValueKind.RefExtern:
                    return new JsonObject
                    {
                        ["type"] = "externref",
                        ["value"] = v.RefId ?? "null",
                    };
                case ScriptValueKind.RefFunc:
                    return new JsonObject
                    {
                        ["type"] = "funcref",
                        ["value"] = v.RefId ?? "null",
                    };
                case ScriptValueKind.RefGeneric:
                    return new JsonObject
                    {
                        ["type"] = RefGenericType(v.RefHeapType),
                        ["value"] = v.RefId ?? "null",
                    };
                case ScriptValueKind.Either:
                {
                    var alts = new JsonArray();
                    if (v.EitherAlternatives != null)
                        foreach (var alt in v.EitherAlternatives)
                            alts.Add(EmitValue(alt));
                    return new JsonObject
                    {
                        ["type"] = "either",
                        ["values"] = alts,
                    };
                }
                default:
                    throw new InvalidOperationException(
                        $"unknown ScriptValueKind {v.Kind}");
            }
        }

        private static string? FloatPatternToString(ScriptFloatPattern pattern) => pattern switch
        {
            ScriptFloatPattern.NanCanonical => "nan:canonical",
            ScriptFloatPattern.NanArithmetic => "nan:arithmetic",
            _ => null,
        };

        private static string NullTypeForHeap(string? heap) => heap switch
        {
            "func" => "funcref",
            "extern" => "externref",
            "any" => "anyref",
            "eq" => "eqref",
            "i31" => "i31ref",
            "struct" => "structref",
            "array" => "arrayref",
            "exn" => "exnref",
            "none" => "nullref",
            "nofunc" => "nullfuncref",
            "noextern" => "nullexternref",
            "noexn" => "nullexnref",
            _ => "refnull",
        };

        private static string RefGenericType(string? heap) => heap switch
        {
            "any" => "anyref",
            "eq" => "eqref",
            "i31" => "i31ref",
            "struct" => "structref",
            "array" => "arrayref",
            _ => heap + "ref",
        };
    }
}
