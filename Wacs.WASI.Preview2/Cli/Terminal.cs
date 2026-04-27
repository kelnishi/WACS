// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Marker resource for <c>wasi:cli/terminal-input@0.2.x</c>.
    /// The WIT type has no methods — it's an opaque handle the
    /// guest can hold to know "this stdin is interactive". Drop
    /// is the only operation.
    /// </summary>
    [WasiResource("terminal-input")]
    public class TerminalInput : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>Marker resource for
    /// <c>wasi:cli/terminal-output@0.2.x</c>.</summary>
    [WasiResource("terminal-output")]
    public class TerminalOutput : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>Host-side surface for
    /// <c>wasi:cli/terminal-stdin@0.2.x</c>.
    /// <code>
    /// interface terminal-stdin {
    ///     get-terminal-stdin: func() -&gt; option&lt;terminal-input&gt;;
    /// }
    /// </code>
    /// Returns null when stdin isn't a terminal.</summary>
    public interface ITerminalStdin
    {
        TerminalInput? GetTerminalStdin();
    }

    public interface ITerminalStdout
    {
        TerminalOutput? GetTerminalStdout();
    }

    public interface ITerminalStderr
    {
        TerminalOutput? GetTerminalStderr();
    }

    /// <summary>Default <see cref="ITerminalStdin"/> impl using
    /// <see cref="System.Console.IsInputRedirected"/> to detect
    /// whether stdin is bound to a terminal. Returns null
    /// (option None) when redirected.</summary>
    public sealed class TerminalStdin : ITerminalStdin
    {
        public TerminalInput? GetTerminalStdin() =>
            Console.IsInputRedirected ? null : new TerminalInput();
    }

    public sealed class TerminalStdout : ITerminalStdout
    {
        public TerminalOutput? GetTerminalStdout() =>
            Console.IsOutputRedirected ? null : new TerminalOutput();
    }

    public sealed class TerminalStderr : ITerminalStderr
    {
        public TerminalOutput? GetTerminalStderr() =>
            Console.IsErrorRedirected ? null : new TerminalOutput();
    }
}
