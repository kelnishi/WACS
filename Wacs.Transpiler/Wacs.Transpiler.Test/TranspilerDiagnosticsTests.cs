// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.IO;
using Wacs.Transpiler;
using Wacs.Transpiler.Cli;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Locks the v0 wasi-gfx debugging UX regression — when the
    /// transpiler's direct-link emit silently rejects a binding or
    /// the lenient IImports stub silently serves a default, both
    /// must surface on stderr under
    /// <c>WACS_TRANSPILER_DEBUG</c> / the
    /// <c>TranspilerDiagnostics.Enabled</c> toggle. Without these
    /// logs, "wasm hangs at 100% CPU with no output" is the only
    /// symptom of an unresolved import and debugging takes hours.
    /// </summary>
    public class TranspilerDiagnosticsTests
    {
        // Tiny IImports stand-in so we don't depend on an actual
        // transpiled component for this unit.
        public interface IFakeImports
        {
            int Poll(int handle);
            void Spawn();
        }

        [Fact]
        public void LenientServed_logs_first_invoke_to_stderr()
        {
            var prevEnabled = TranspilerDiagnostics.Enabled;
            var prevError = Console.Error;
            try
            {
                TranspilerDiagnostics.Enabled = true;
                TranspilerDiagnostics.ResetSeen();
                using var captured = new StringWriter();
                Console.SetError(captured);

                // Fully qualify — the test project has a local
                // ImportDispatcher stand-in in the same namespace.
                var stub = (IFakeImports)
                    global::Wacs.Transpiler.Cli.ImportDispatcher
                        .Create(
                            typeof(IFakeImports),
                            new System.Collections.Generic.Dictionary<
                                string, Func<object?[], object?>>(),
                            true);

                // Two calls to the SAME method — should log once.
                var r1 = stub.Poll(0);
                var r2 = stub.Poll(0);

                var text = captured.ToString();
                Assert.Equal(0, r1);
                Assert.Equal(0, r2);
                Assert.Contains("wacs-transpiler", text);
                Assert.Contains("lenient default served", text);
                Assert.Contains(nameof(IFakeImports.Poll), text);

                // Dedupe check: exactly one matching line, not two.
                int hits = 0;
                int idx = 0;
                while ((idx = text.IndexOf("lenient default served",
                    idx, StringComparison.Ordinal)) >= 0)
                {
                    hits++;
                    idx += 1;
                }
                Assert.Equal(1, hits);
            }
            finally
            {
                Console.SetError(prevError);
                TranspilerDiagnostics.Enabled = prevEnabled;
            }
        }

        [Fact]
        public void LenientServed_silent_when_disabled()
        {
            var prevEnabled = TranspilerDiagnostics.Enabled;
            var prevError = Console.Error;
            try
            {
                TranspilerDiagnostics.Enabled = false;
                TranspilerDiagnostics.ResetSeen();
                using var captured = new StringWriter();
                Console.SetError(captured);

                // Fully qualify — the test project has a local
                // ImportDispatcher stand-in in the same namespace.
                var stub = (IFakeImports)
                    global::Wacs.Transpiler.Cli.ImportDispatcher
                        .Create(
                            typeof(IFakeImports),
                            new System.Collections.Generic.Dictionary<
                                string, Func<object?[], object?>>(),
                            true);
                stub.Spawn();
                stub.Poll(0);

                Assert.Equal(string.Empty, captured.ToString());
            }
            finally
            {
                Console.SetError(prevError);
                TranspilerDiagnostics.Enabled = prevEnabled;
            }
        }

        [Fact]
        public void EmitGateReject_logs_gate_and_reason()
        {
            var prevEnabled = TranspilerDiagnostics.Enabled;
            var prevError = Console.Error;
            try
            {
                TranspilerDiagnostics.Enabled = true;
                TranspilerDiagnostics.ResetSeen();
                using var captured = new StringWriter();
                Console.SetError(captured);

                TranspilerDiagnostics.TraceEmitGateReject(
                    module: "wasi:io/poll@0.2.8",
                    entity: "poll",
                    gate: "canon-abi-unsupported-param",
                    reason: "param[0] CLR type IPollable[] has no slot mapping");

                var text = captured.ToString();
                Assert.Contains("wacs-transpiler", text);
                Assert.Contains("wasi:io/poll@0.2.8", text);
                Assert.Contains("poll", text);
                Assert.Contains("canon-abi-unsupported-param", text);
                Assert.Contains("IPollable[]", text);
            }
            finally
            {
                Console.SetError(prevError);
                TranspilerDiagnostics.Enabled = prevEnabled;
            }
        }

        [Fact]
        public void EmitGateReject_dedupes_per_module_entity_gate()
        {
            var prevEnabled = TranspilerDiagnostics.Enabled;
            var prevError = Console.Error;
            try
            {
                TranspilerDiagnostics.Enabled = true;
                TranspilerDiagnostics.ResetSeen();
                using var captured = new StringWriter();
                Console.SetError(captured);

                // Repeat the same gate failure three times.
                for (int i = 0; i < 3; i++)
                {
                    TranspilerDiagnostics.TraceEmitGateReject(
                        "wasi:io/poll@0.2.8", "poll", "g", "r");
                }

                var text = captured.ToString();
                int hits = 0;
                int idx = 0;
                while ((idx = text.IndexOf("direct-link emit rejected",
                    idx, StringComparison.Ordinal)) >= 0)
                {
                    hits++;
                    idx += 1;
                }
                Assert.Equal(1, hits);
            }
            finally
            {
                Console.SetError(prevError);
                TranspilerDiagnostics.Enabled = prevEnabled;
            }
        }
    }
}
