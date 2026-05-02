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
using System.Text;
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Text;

namespace Spec.Test
{
    /// <summary>
    /// Bridges the WACS native <see cref="TextScriptParser"/> output
    /// (<see cref="ScriptCommand"/> tree) onto the JSON-shaped runner
    /// (<see cref="WastJson.WastJson"/> + <see cref="ICommand"/>).
    ///
    /// Lets the spec-test pipeline drop its dependency on
    /// <c>wasm-tools json-from-wast</c>: rather than precompiling
    /// every <c>.wast</c> file to JSON + .wasm artifacts on disk,
    /// the test data provider parses each <c>.wast</c> in-process
    /// and produces ICommand objects backed by pre-parsed
    /// <see cref="Module"/> instances.
    ///
    /// Modules referenced from assertion forms (e.g. the inner
    /// <c>(module …)</c> of an <c>assert_invalid</c>) carry both a
    /// <c>PreParsedModule</c> on the assertion command (when parse
    /// succeeded) and the captured <c>PreParseError</c> exception
    /// (when it failed) so the assertion can render its verdict
    /// without a re-parse.
    /// </summary>
    public static class WastScriptAdapter
    {
        public static WastJson.WastJson FromWastFile(string path, bool traceExecution = false)
        {
            var source = File.ReadAllText(path);
            // Branch hints are advisory but we want them surfaced for
            // any spec test that uses them — turning the flag on here
            // is a no-op for the vast majority of files.
            BinaryModuleParser.ParseBranchHints = true;
            var script = TextScriptParser.ParseWast(source);

            var result = new WastJson.WastJson
            {
                SourceFilename = path,
                Path = Path.GetDirectoryName(path) ?? string.Empty,
                Commands = new List<ICommand>(script.Count),
                TraceExecution = traceExecution,
            };

            foreach (var cmd in script)
            {
                var converted = Convert(cmd);
                if (converted != null)
                    result.Commands.Add(converted);
            }
            return result;
        }

        private static ICommand? Convert(ScriptCommand cmd)
        {
            switch (cmd)
            {
                case ScriptModule sm:        return ConvertModule(sm);
                case ScriptRegister sr:      return ConvertRegister(sr);
                case ScriptInvoke si:        return ConvertInvokeAction(si);
                case ScriptGet sg:           return ConvertGetAction(sg);
                case ScriptAssertReturn ar:  return ConvertAssertReturn(ar);
                case ScriptAssertTrap at:    return ConvertAssertTrap(at);
                case ScriptAssertExhaustion ae: return ConvertAssertExhaustion(ae);
                case ScriptAssertInvalid ai: return ConvertAssertInvalid(ai);
                case ScriptAssertMalformed am: return ConvertAssertMalformed(am);
                case ScriptAssertUnlinkable au: return ConvertAssertUnlinkable(au);
                case ScriptAssertException axe: return ConvertAssertException(axe);
                default: return null;
            }
        }

        private static ICommand ConvertModule(ScriptModule sm)
        {
            // Binary or Quote modules: re-parse the bytes as a binary
            // module if needed. Quote modules already eagerly parse
            // their text in TextScriptParser; reuse that result.
            Module? parsed = sm.Module;
            if (parsed == null && sm.Kind == ScriptModuleKind.Binary && sm.Bytes != null)
            {
                using var ms = new MemoryStream(sm.Bytes);
                parsed = BinaryModuleParser.ParseWasm(ms);
            }
            return new ModuleCommand
            {
                Line = sm.Line,
                Name = sm.Id,
                PreParsedModule = parsed,
            };
        }

        private static ICommand ConvertRegister(ScriptRegister sr) => new RegisterCommand
        {
            Line = sr.Line,
            Name = sr.ModuleId,
            As = sr.ExportName,
        };

        private static ICommand ConvertInvokeAction(ScriptInvoke si) => new ActionCommand
        {
            Line = si.Line,
            Action = ScriptInvokeToInvokeAction(si),
        };

        private static ICommand ConvertGetAction(ScriptGet sg) => new ActionCommand
        {
            Line = sg.Line,
            Action = new GetAction { Field = sg.ExportName },
        };

        private static ICommand ConvertAssertReturn(ScriptAssertReturn ar) => new AssertReturnCommand
        {
            Line = ar.Line,
            Action = ScriptActionToIAction(ar.Action),
            Expected = ScriptValuesToArguments(ar.Expected),
        };

        private static ICommand ConvertAssertTrap(ScriptAssertTrap at) => new AssertTrapCommand
        {
            Line = at.Line,
            Action = at.Action != null ? ScriptActionToIAction(at.Action) : null,
            Text = at.ExpectedMessage,
        };

        private static ICommand ConvertAssertExhaustion(ScriptAssertExhaustion ae) => new AssertExhaustionCommand
        {
            Line = ae.Line,
            Action = ScriptActionToIAction(ae.Action),
            Text = ae.ExpectedMessage,
        };

        private static ICommand ConvertAssertInvalid(ScriptAssertInvalid ai)
        {
            var (mod, err) = ParseInnerOrCaptureError(ai.Module);
            return new AssertInvalidCommand
            {
                Line = ai.Line,
                Text = ai.ExpectedMessage,
                PreParsedModule = mod,
                PreParseError = err,
            };
        }

        private static ICommand ConvertAssertMalformed(ScriptAssertMalformed am)
        {
            var (mod, err) = ParseInnerOrCaptureError(am.Module);
            return new AssertMalformedCommand
            {
                Line = am.Line,
                Text = am.ExpectedMessage,
                PreParsedModule = mod,
                PreParseError = err,
            };
        }

        private static ICommand ConvertAssertUnlinkable(ScriptAssertUnlinkable au)
        {
            var (mod, err) = ParseInnerOrCaptureError(au.Module);
            return new AssertUnlinkableCommand
            {
                Line = au.Line,
                Text = au.ExpectedMessage,
                PreParsedModule = mod,
                PreParseError = err,
            };
        }

        private static ICommand ConvertAssertException(ScriptAssertException axe) => new AssertExceptionCommand
        {
            Line = axe.Line,
            Action = ScriptActionToIAction(axe.Action),
        };

        // --------------------------------------------------------
        // Helpers
        // --------------------------------------------------------

        /// <summary>
        /// For inner modules of assert_*: try to surface a parsed
        /// Module if the script parser already produced one, or
        /// re-parse on demand. Quote-form modules in TextScriptParser
        /// already swallow parse errors and yield Module=null; we
        /// don't have the original exception in that case, so we
        /// re-parse the quoted text here to capture it.
        /// </summary>
        private static (Module? Module, Exception? Error) ParseInnerOrCaptureError(ScriptModule sm)
        {
            if (sm.Module != null)
                return (sm.Module, null);

            // Quote module that failed eager parse — re-run to capture
            // the FormatException for assertion verdicts.
            if (sm.Kind == ScriptModuleKind.Quote && sm.Bytes != null)
            {
                try
                {
                    var parsed = TextModuleParser.ParseWat(Encoding.UTF8.GetString(sm.Bytes));
                    return (parsed, null);
                }
                catch (Exception e) { return (null, e); }
            }

            // Binary module: reparse from bytes; capture errors.
            if (sm.Kind == ScriptModuleKind.Binary && sm.Bytes != null)
            {
                try
                {
                    using var ms = new MemoryStream(sm.Bytes);
                    var parsed = BinaryModuleParser.ParseWasm(ms);
                    return (parsed, null);
                }
                catch (Exception e) { return (null, e); }
            }

            return (null, null);
        }

        private static InvokeAction ScriptInvokeToInvokeAction(ScriptInvoke si)
        {
            var action = new InvokeAction
            {
                Field = si.ExportName,
                Module = si.ModuleId ?? string.Empty,
            };
            foreach (var arg in si.Args)
                action.Args.Add(ScriptValueToArgument(arg));
            return action;
        }

        private static IAction ScriptActionToIAction(ScriptAction sa)
        {
            return sa switch
            {
                ScriptInvoke si => ScriptInvokeToInvokeAction(si),
                ScriptGet sg   => new GetAction { Field = sg.ExportName },
                _ => throw new NotSupportedException(
                    $"Unsupported ScriptAction subtype {sa.GetType().Name}"),
            };
        }

        private static List<Argument> ScriptValuesToArguments(IEnumerable<ScriptValue> vals)
        {
            var list = new List<Argument>();
            foreach (var v in vals)
                list.Add(ScriptValueToArgument(v));
            return list;
        }

        /// <summary>
        /// Lower a typed <see cref="ScriptValue"/> to the JSON-shaped
        /// <see cref="Argument"/> the existing runner expects. The
        /// JSON shape always carries the value as a STRING (parsed
        /// downstream by <see cref="Wacs.Core.Runtime.Value"/>) — we
        /// reproduce that convention so the runner's downstream code
        /// works unchanged.
        /// </summary>
        private static Argument ScriptValueToArgument(ScriptValue sv)
        {
            switch (sv.Kind)
            {
                case ScriptValueKind.I32:
                    return new Argument { Type = "i32", Value = unchecked((uint)sv.I32).ToString() };
                case ScriptValueKind.I64:
                    return new Argument { Type = "i64", Value = unchecked((ulong)sv.I64).ToString() };
                case ScriptValueKind.F32:
                    return F32ToArgument(sv);
                case ScriptValueKind.F64:
                    return F64ToArgument(sv);
                case ScriptValueKind.RefNull:
                    return RefNullKindToArgument(sv);
                case ScriptValueKind.RefExtern:
                    return new Argument { Type = "externref", Value = sv.RefId ?? "null" };
                case ScriptValueKind.RefFunc:
                    return new Argument { Type = "funcref", Value = sv.RefId ?? "null" };
                case ScriptValueKind.RefGeneric:
                    return RefGenericKindToArgument(sv);
                case ScriptValueKind.V128:
                    return V128ToArgument(sv);
                default:
                    throw new NotSupportedException($"ScriptValue kind {sv.Kind}");
            }
        }

        private static Argument F32ToArgument(ScriptValue sv)
        {
            if (sv.FloatPattern == ScriptFloatPattern.NanCanonical)
                return new Argument { Type = "f32", Value = "nan:canonical" };
            if (sv.FloatPattern == ScriptFloatPattern.NanArithmetic)
                return new Argument { Type = "f32", Value = "nan:arithmetic" };
            // Use the raw bits captured by ParseFloatLiteral so NaN
            // payloads and hex-float precision survive.
            return new Argument { Type = "f32", Value = sv.F32Bits.ToString() };
        }

        private static Argument F64ToArgument(ScriptValue sv)
        {
            if (sv.FloatPattern == ScriptFloatPattern.NanCanonical)
                return new Argument { Type = "f64", Value = "nan:canonical" };
            if (sv.FloatPattern == ScriptFloatPattern.NanArithmetic)
                return new Argument { Type = "f64", Value = "nan:arithmetic" };
            return new Argument { Type = "f64", Value = sv.F64Bits.ToString() };
        }

        private static Argument RefNullKindToArgument(ScriptValue sv)
        {
            // (ref.null <heap>) — the runner has dedicated null
            // sentinel types per heaptype. Use the most specific.
            return sv.RefHeapType switch
            {
                "func"   => new Argument { Type = "nullfuncref",   Value = "null" },
                "extern" => new Argument { Type = "nullexternref", Value = "null" },
                "exn"    => new Argument { Type = "nullexnref",    Value = "null" },
                _        => new Argument { Type = "refnull",       Value = "null" },
            };
        }

        private static Argument RefGenericKindToArgument(ScriptValue sv)
        {
            return sv.RefHeapType switch
            {
                "any"    => new Argument { Type = "anyref",    Value = "null" },
                "struct" => new Argument { Type = "structref", Value = "null" },
                "array"  => new Argument { Type = "arrayref",  Value = "null" },
                "eq"     => new Argument { Type = "eqref",     Value = "null" },
                "i31"    => new Argument { Type = "i31ref",    Value = "null" },
                "exn"    => new Argument { Type = "exnref",    Value = "null" },
                _        => new Argument { Type = "ref",       Value = "null" },
            };
        }

        private static Argument V128ToArgument(ScriptValue sv)
        {
            // The downstream runner expects v128 as a JSON array of
            // lane string values. We don't have a JsonElement here;
            // synthesize one by serializing then re-deserializing.
            // Cheap, runs once per spec command at load time.
            if (sv.V128 == null)
                throw new InvalidDataException("ScriptValue.V128 missing payload");
            // Encode the 16 bytes as 16 i8 lane strings.
            var lanes = new List<string>(16);
            foreach (var b in sv.V128)
                lanes.Add(b.ToString());
            using var ms = new MemoryStream();
            using (var w = new System.Text.Json.Utf8JsonWriter(ms))
            {
                w.WriteStartArray();
                foreach (var lane in lanes) w.WriteStringValue(lane);
                w.WriteEndArray();
            }
            ms.Position = 0;
            using var doc = System.Text.Json.JsonDocument.Parse(ms);
            return new Argument
            {
                Type = "v128",
                LaneType = "i8",
                Value = doc.RootElement.Clone(),
            };
        }
    }
}
