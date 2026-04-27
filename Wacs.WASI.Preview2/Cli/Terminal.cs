// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Marker resource for <c>wasi:cli/terminal-input@0.2.x</c>.
    /// The WIT type has no methods — it's an opaque handle the
    /// guest can hold to know "this stdin is interactive". Drop
    /// is the only operation. Implements the generated
    /// <see cref="ITerminalInput"/> marker so cross-iface refs
    /// (<see cref="ITerminalStdin.GetTerminalStdin"/>'s
    /// <c>Option&lt;ITerminalInput&gt;</c>) accept this concrete
    /// type.
    /// </summary>
    [WasiResource("terminal-input")]
    public class TerminalInput : ITerminalInput, IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>Marker resource for
    /// <c>wasi:cli/terminal-output@0.2.x</c>. Implements the
    /// generated <see cref="ITerminalOutput"/> marker.</summary>
    [WasiResource("terminal-output")]
    public class TerminalOutput : ITerminalOutput, IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>Default <see cref="ITerminalStdin"/> impl using
    /// <see cref="System.Console.IsInputRedirected"/> to detect
    /// whether stdin is bound to a terminal. Returns
    /// <c>Option.None</c> when redirected.</summary>
    public sealed class TerminalStdin : ITerminalStdin
    {
        public Option<ITerminalInput> GetTerminalStdin() =>
            Console.IsInputRedirected
                ? Option<ITerminalInput>.None
                : Option<ITerminalInput>.Some(new TerminalInput());
    }

    public sealed class TerminalStdout : ITerminalStdout
    {
        public Option<ITerminalOutput> GetTerminalStdout() =>
            Console.IsOutputRedirected
                ? Option<ITerminalOutput>.None
                : Option<ITerminalOutput>.Some(new TerminalOutput());
    }

    public sealed class TerminalStderr : ITerminalStderr
    {
        public Option<ITerminalOutput> GetTerminalStderr() =>
            Console.IsErrorRedirected
                ? Option<ITerminalOutput>.None
                : Option<ITerminalOutput>.Some(new TerminalOutput());
    }
}
