// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.HostBinding;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Tests for wasi:cli/terminal-* — exercise the new
    /// option&lt;own&lt;T&gt;&gt; canon-lower wrapper.
    /// </summary>
    public class TerminalTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        private sealed class StubTerminalStdin : ITerminalStdin
        {
            private readonly bool _isTerminal;
            public StubTerminalStdin(bool isTerminal) { _isTerminal = isTerminal; }
            public TerminalInput? GetTerminalStdin() =>
                _isTerminal ? new TerminalInput() : null;
        }

        [Fact]
        public void GetTerminalStdin_returns_None_when_redirected()
        {
            // Fixture's check() exports the disc byte from the
            // option<terminal-input> retArea. Stub returns null
            // → disc=0 → check() returns 0.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-terminal-stdin-component", "term.component.wasm"));
            var resources = new ResourceContext();
            var stub = new StubTerminalStdin(isTerminal: false);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new CliBindings(resources, terminalStdin: stub)
                    .BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke("check")!);
        }

        [Fact]
        public void GetTerminalStdin_returns_Some_when_attached()
        {
            // Stub returns a fresh TerminalInput → disc=1 →
            // check() returns 1 + drops the handle (table empty).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-terminal-stdin-component", "term.component.wasm"));
            var resources = new ResourceContext();
            var stub = new StubTerminalStdin(isTerminal: true);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new CliBindings(resources, terminalStdin: stub)
                    .BindToRuntime(runtime));

            Assert.Equal(1u, (uint)ci.Invoke("check")!);
            Assert.Equal(0, resources.TableFor(typeof(TerminalInput)).Count);
        }
    }
}
