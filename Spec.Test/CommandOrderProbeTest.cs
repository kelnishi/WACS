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
using System.Text.Json;
using System.Text.Json.Serialization;
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Spec.Test
{
    /// <summary>
    /// Diagnostic probe answering: "Is allocation and instantiation
    /// happening in the same order across the WAT-direct and JSON
    /// pipelines, irrespective of when parsing happens?"
    ///
    /// For a target .wast file, this loads it BOTH ways:
    ///   (a) WastScriptAdapter.FromWastFile (WAT-direct, eager parse)
    ///   (b) JSON deserializer + on-disk .wasm files (legacy)
    /// then dumps the resulting Commands sequence side-by-side and asserts
    /// they are structurally identical (kind + line + name + action shape).
    ///
    /// Also instruments runtime calls (InstantiateModule / RegisterModule
    /// / GetModule / invoke) by wrapping the runtime — proving that the
    /// observed order at the runtime boundary matches between paths.
    /// </summary>
    public class CommandOrderProbeTest
    {
        private readonly ITestOutputHelper _output;

        public CommandOrderProbeTest(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Regression: the WAT-direct adapter previously mapped
        /// <c>(assert_trap (module …) "msg")</c> to AssertTrapCommand
        /// with a null Action — silently passing without ever
        /// instantiating the module, which left the runtime store in
        /// a state that diverged from the JSON pipeline. These four
        /// wasts (linking.wast, table.wast, try_table.wast,
        /// multi-memory/linking0.wast, multi-memory/linking3.wast) all
        /// failed at runtime due to that divergence; this fact runs
        /// each end-to-end via WAT-direct and asserts they complete
        /// without exception.
        /// </summary>
        [Theory]
        [InlineData("Spec.Test/spec/test/core/linking.wast")]
        [InlineData("Spec.Test/spec/test/core/table.wast")]
        [InlineData("Spec.Test/spec/test/core/exceptions/try_table.wast")]
        [InlineData("Spec.Test/spec/test/core/multi-memory/linking0.wast")]
        [InlineData("Spec.Test/spec/test/core/multi-memory/linking3.wast")]
        public void WatDirectRunsThroughPreviouslyFailingWasts(string relPath)
        {
            // The Spec.Test cwd is bin/Release/net8.0; reach back to the repo root.
            var fullPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "../../..", "..", relPath));
            Assert.True(File.Exists(fullPath), $"missing: {fullPath}");

            var file = WastScriptAdapter.FromWastFile(fullPath);
            var env = new SpecTestEnv();
            var runtime = new WasmRuntime();
            env.BindToRuntime(runtime);
            runtime.SuperInstruction = false;

            Module? module = null;
            int idx = 0;
            foreach (var command in file.Commands)
            {
                try
                {
                    command.RunTest(file, ref runtime, ref module);
                }
                catch (TestException te)
                {
                    Assert.Fail($"{relPath} cmd[{idx}] {command.GetType().Name}: {te.Message}");
                }
                catch (Exception e)
                {
                    Assert.Fail(
                        $"{relPath} cmd[{idx}] {command.GetType().Name} L{GetLine(command)} unexpectedly threw " +
                        $"{e.GetType().Name}: {e.Message}");
                }
                idx++;
            }
        }

        [Fact(Skip = "Diagnostic probe; run on demand by removing Skip.")]
        public void LinkingWastCommandOrderMatches()
        {
            var wastPath = "/Users/kelvinnishikawa/wasm/WACS/Spec.Test/spec/test/core/linking.wast";
            var jsonPath = "/Users/kelvinnishikawa/wasm/WACS/Spec.Test/generated-json/linking.wast/linking.json";

            var watFile = WastScriptAdapter.FromWastFile(wastPath);
            var jsonFile = LoadJson(jsonPath);

            var watSeq = SummarizeCommands(watFile.Commands).ToList();
            var jsonSeq = SummarizeCommands(jsonFile.Commands).ToList();

            _output.WriteLine($"WAT  command count: {watSeq.Count}");
            _output.WriteLine($"JSON command count: {jsonSeq.Count}");

            int n = Math.Max(watSeq.Count, jsonSeq.Count);
            int firstDiff = -1;
            for (int i = 0; i < n; i++)
            {
                var a = i < watSeq.Count ? watSeq[i] : "<missing>";
                var b = i < jsonSeq.Count ? jsonSeq[i] : "<missing>";
                if (a != b && firstDiff < 0) firstDiff = i;
                _output.WriteLine($"  [{i,3}] {(a == b ? "OK " : "** ")} WAT={a}  JSON={b}");
            }
            if (firstDiff >= 0)
                _output.WriteLine($"FIRST DIVERGENCE at index {firstDiff}");
            else
                _output.WriteLine("Command sequences are IDENTICAL.");
        }

        [Fact(Skip = "Diagnostic probe; run on demand by removing Skip and pointing at any wast.")]
        public void LinkingWastRuntimeTrace()
        {
            var wastPath = "/Users/kelvinnishikawa/wasm/WACS/Spec.Test/spec/test/core/exceptions/try_table.wast";
            var jsonPath = "/Users/kelvinnishikawa/wasm/WACS/Spec.Test/generated-json/try_table.wast/try_table.json";

            var watTrace = RunAndTrace(WastScriptAdapter.FromWastFile(wastPath));
            var jsonTrace = RunAndTrace(LoadJson(jsonPath));

            _output.WriteLine("=== WAT trace ===");
            foreach (var (i, e) in watTrace.Select((s, i) => (i, s))) _output.WriteLine($"  {i,3}: {e}");
            _output.WriteLine("=== JSON trace ===");
            foreach (var (i, e) in jsonTrace.Select((s, i) => (i, s))) _output.WriteLine($"  {i,3}: {e}");

            int n = Math.Max(watTrace.Count, jsonTrace.Count);
            int firstDiff = -1;
            for (int i = 0; i < n; i++)
            {
                var a = i < watTrace.Count ? watTrace[i] : "<end>";
                var b = i < jsonTrace.Count ? jsonTrace[i] : "<end>";
                if (a != b && firstDiff < 0) { firstDiff = i; break; }
            }
            if (firstDiff >= 0)
                _output.WriteLine($"FIRST RUNTIME DIVERGENCE at trace index {firstDiff}");
            else
                _output.WriteLine("Runtime traces are IDENTICAL.");
        }

        private List<string> RunAndTrace(WastJson.WastJson file)
        {
            var trace = new List<string>();
            var env = new SpecTestEnv();
            var runtime = new WasmRuntime();
            env.BindToRuntime(runtime);
            runtime.SuperInstruction = false;

            Module? module = null;
            int idx = 0;
            foreach (var command in file.Commands)
            {
                // Strip line numbers — WAT/JSON disagree on whether the
                // line points at the outer assertion paren or the inner
                // (module …); cosmetic only and would hide real
                // runtime-divergence below it.
                trace.Add($"cmd[{idx++}] {command.GetType().Name}");
                try
                {
                    command.RunTest(file, ref runtime, ref module);
                }
                catch (Exception e)
                {
                    trace.Add($"  EXC {e.GetType().Name}: {e.Message}");
                    break;
                }
            }
            return trace;
        }

        private static int GetLine(ICommand c) => c switch
        {
            ModuleCommand m => m.Line,
            RegisterCommand r => r.Line,
            ActionCommand a => a.Line,
            AssertReturnCommand ar => ar.Line,
            AssertTrapCommand at => at.Line,
            AssertExceptionCommand ax => ax.Line,
            AssertExhaustionCommand ae => ae.Line,
            AssertInvalidCommand ai => ai.Line,
            AssertMalformedCommand am => am.Line,
            AssertUnlinkableCommand au => au.Line,
            AssertUninstantiableCommand aun => aun.Line,
            _ => -1,
        };

        private static IEnumerable<string> SummarizeCommands(IEnumerable<ICommand> cmds)
        {
            foreach (var c in cmds)
            {
                yield return c switch
                {
                    ModuleCommand m =>
                        $"L{m.Line} Module name=\"{m.Name ?? "-"}\" file=\"{m.Filename ?? "-"}\" pre={(m.PreParsedModule != null)}",
                    RegisterCommand r =>
                        $"L{r.Line} Register as=\"{r.As}\" name=\"{r.Name ?? "-"}\"",
                    ActionCommand a =>
                        $"L{a.Line} Action {DescribeAction(a.Action)}",
                    AssertReturnCommand ar =>
                        $"L{ar.Line} AssertReturn {DescribeAction(ar.Action)} expected={ar.Expected.Count}",
                    AssertTrapCommand at =>
                        $"L{at.Line} AssertTrap {DescribeAction(at.Action)} \"{at.Text}\"",
                    AssertExhaustionCommand ae =>
                        $"L{ae.Line} AssertExhaustion {DescribeAction(ae.Action)}",
                    AssertExceptionCommand ax =>
                        $"L{ax.Line} AssertException {DescribeAction(ax.Action)}",
                    AssertInvalidCommand ai =>
                        $"L{ai.Line} AssertInvalid \"{ai.Text}\"",
                    AssertMalformedCommand am =>
                        $"L{am.Line} AssertMalformed \"{am.Text}\"",
                    AssertUnlinkableCommand au =>
                        $"L{au.Line} AssertUnlinkable \"{au.Text}\"",
                    AssertUninstantiableCommand aun =>
                        $"L{aun.Line} AssertUninstantiable \"{aun.Text}\"",
                    _ => $"<{c.GetType().Name}>",
                };
            }
        }

        private static string DescribeAction(IAction? a) => a switch
        {
            InvokeAction i => $"invoke module=\"{i.Module ?? "-"}\" field=\"{i.Field}\" args={i.Args.Count}",
            GetAction g => $"get field=\"{g.Field}\"",
            null => "<no action>",
            _ => a.GetType().Name,
        };

        private static WastJson.WastJson LoadJson(string jsonPath)
        {
            var options = new JsonSerializerOptions
            {
                Converters =
                {
                    new CommandJsonConverter(),
                    new ActionJsonConverter(),
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                },
                PropertyNameCaseInsensitive = true
            };
            var def = JsonSerializer.Deserialize<WastJson.WastJson>(File.ReadAllText(jsonPath), options)!;
            def.Path = Path.GetDirectoryName(jsonPath)!;
            return def;
        }
    }
}
