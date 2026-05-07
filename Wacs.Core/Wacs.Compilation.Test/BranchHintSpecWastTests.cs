// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Compilation.Test
{
    /// <summary>
    /// Runs the upstream Branch Hinting proposal's spec test
    /// (<c>spec/test/custom/metadata.code.branch_hint/branch_hint.wast</c>)
    /// directly through WACS's own WAT/WAST parser — no wabt or
    /// wasm-tools dependency. The .wast uses
    /// <c>assert_malformed_custom</c> and <c>assert_invalid_custom</c>
    /// directives that neither external tool understands; lowering
    /// those to the same code path as <c>assert_malformed</c> /
    /// <c>assert_invalid</c> is part of this PR's WAST parser changes.
    /// </summary>
    public class BranchHintSpecWastTests
    {
        private static string LocateWastFile()
        {
            // Walk up from the test bin/ to the repo root, then resolve
            // the spec submodule path.
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "WACS.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            var path = Path.Combine(dir!, "Spec.Test", "spec", "test", "custom",
                "metadata.code.branch_hint", "branch_hint.wast");
            Assert.True(File.Exists(path),
                $"spec submodule fixture not found at {path} — run `git submodule update --init`");
            return path;
        }

        [Fact]
        public void RunBranchHintSpecWast()
        {
            // Branch-hint annotations are only honored when the parser
            // flag is on (interpreter consumers skip them). The spec
            // file's whole point is exercising that path.
            BinaryModuleParser.ParseBranchHints = true;

            var source = File.ReadAllText(LocateWastFile());
            var commands = TextScriptParser.ParseWast(source);

            int modulesParsed = 0;
            int malformedAssertsChecked = 0;
            int invalidAssertsChecked = 0;

            foreach (var cmd in commands)
            {
                switch (cmd)
                {
                    case ScriptModule sm:
                    {
                        Assert.NotNull(sm.Module);
                        // The valid module declares hinted `if` instructions.
                        // Confirm the WAT parser captured at least one hint.
                        Assert.NotNull(sm.Module!.BranchHints);
                        Assert.NotEmpty(sm.Module.BranchHints!.ByInstruction);
                        modulesParsed++;
                        break;
                    }
                    case ScriptAssertMalformed am:
                    {
                        // The expected message is one of:
                        //   "@metadata.code.branch_hint annotation: duplicate annotation"
                        //   "@metadata.code.branch_hint annotation: not in a function"
                        // Either way: parse must FAIL — i.e. eager
                        // parse during command construction yielded
                        // null Module, or a re-parse throws.
                        AssertModuleRejected(am.Module, am.ExpectedMessage,
                            "assert_malformed_custom");
                        malformedAssertsChecked++;
                        break;
                    }
                    case ScriptAssertInvalid ai:
                    {
                        // Expected: "@metadata.code.branch_hint annotation: invalid target"
                        AssertModuleRejected(ai.Module, ai.ExpectedMessage,
                            "assert_invalid_custom");
                        invalidAssertsChecked++;
                        break;
                    }
                    default:
                        Assert.Fail($"unexpected command in branch_hint.wast: {cmd.GetType().Name}");
                        break;
                }
            }

            // Sanity-check the file shape — if the upstream test grows
            // new commands we want to know.
            Assert.Equal(1, modulesParsed);
            Assert.Equal(2, malformedAssertsChecked);
            Assert.Equal(1, invalidAssertsChecked);
        }

        private static void AssertModuleRejected(
            ScriptModule sm, string expectedMessageHint, string assertion)
        {
            // Eager parse at script-parse time may have already
            // captured the failure as Module == null. If so, we're done.
            if (sm.Module == null)
                return;

            // Otherwise, force a re-parse and expect a FormatException.
            // (Quote modules: re-parse the bytes; text modules: nothing
            // to re-parse — Module being non-null already means the
            // assertion's expectation was wrong.)
            if (sm.Kind == ScriptModuleKind.Quote)
            {
                Assert.NotNull(sm.Bytes);
                var text = System.Text.Encoding.UTF8.GetString(sm.Bytes!);
                var ex = Assert.Throws<FormatException>(
                    () => TextModuleParser.ParseWat(text));
                Assert.Contains(KeyTermFromHint(expectedMessageHint), ex.Message);
                return;
            }
            // Binary modules within these asserts aren't expected.
            Assert.Fail(
                $"{assertion} expected module to be rejected at parse time " +
                $"(line {sm.Line}); got a non-null parsed Module of kind {sm.Kind}.");
        }

        private static string KeyTermFromHint(string spec)
        {
            // The spec's expected message is a free-form English
            // string. Match on the salient keyword so we don't
            // brittle-couple to the spec runner's exact phrasing.
            if (spec.Contains("duplicate")) return "duplicate annotation";
            if (spec.Contains("not in a function")) return "not in a function";
            if (spec.Contains("invalid target")) return "invalid target";
            return spec;
        }
    }
}
