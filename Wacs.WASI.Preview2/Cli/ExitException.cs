// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Thrown by <see cref="Exit"/> when a guest calls
    /// <c>wasi:cli/exit.exit(...)</c>. Carries the exit code so
    /// the host runtime can map it to a process-exit / propagated
    /// status / etc. WASI guests treat the exit calls as
    /// terminal — divergent control flow that never returns —
    /// so the host's natural translation is an exception that
    /// unwinds the wasm stack.
    /// </summary>
    public sealed class ExitException : Exception
    {
        /// <summary>0 for success, non-zero for failure. The
        /// component-level <c>result</c> form maps Ok→0 and
        /// Err→1; the <c>exit-with-code</c> variant carries the
        /// raw u8 the guest provided.</summary>
        public byte ExitCode { get; }

        public ExitException(byte exitCode)
            : base("WASI guest invoked wasi:cli/exit with code " + exitCode)
        {
            ExitCode = exitCode;
        }
    }
}
