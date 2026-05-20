// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview3.Cli
{
    /// <summary>
    /// Host interface for <c>wasi:cli/exit@0.3.0-rc-2026-03-15</c>.
    /// Single-method interface — the wasip3 spec shrank to just
    /// <c>exit: func(status: result);</c> (the Preview 2
    /// <c>exit-with-code</c> variant was dropped). The
    /// <c>result&lt;_, _&gt;</c> discriminant lowers to a single
    /// i32 over the wire: Ok → 0, Err → 1.
    /// </summary>
    public interface IExit
    {
        /// <summary>Exit with a result-shaped status. Ok → exit
        /// code 0; Err → exit code 1. WASI guests treat this
        /// as terminal — divergent control flow that never
        /// returns — so the host's natural translation is to
        /// throw an exception that unwinds the wasm stack.</summary>
        void Exit(bool isOk);
    }

    /// <summary>
    /// Default <see cref="IExit"/> implementation — throws
    /// <see cref="ExitException"/>. Class name is
    /// <c>ExitHandler</c> rather than <c>Exit</c> to avoid the
    /// C# constructor-name clash with the
    /// <see cref="IExit.Exit"/> method. Hosts that want to
    /// integrate with their environment's exit semantics
    /// (e.g. <c>System.Environment.Exit</c>) should substitute
    /// their own <see cref="IExit"/> impl.
    /// </summary>
    public sealed class ExitHandler : IExit
    {
        public void Exit(bool isOk)
        {
            throw new ExitException(isOk ? (byte)0 : (byte)1);
        }
    }
}
