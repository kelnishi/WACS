// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Host-side surface for <c>wasi:cli/exit@0.2.x</c>.
    /// <code>
    /// interface exit {
    ///     exit: func(status: result);
    ///     exit-with-code: func(status-code: u8);  // unstable
    /// }
    /// </code>
    ///
    /// <para>The <c>status: result</c> param is a no-payload
    /// discriminator — Ok (true) maps to exit code 0, Err
    /// (false) maps to 1. The unstable <c>exit-with-code</c>
    /// variant carries an explicit u8 instead.</para>
    ///
    /// <para>Both methods are divergent — the spec says they
    /// "exit the current instance" without returning. The
    /// natural host translation is to throw a host-defined
    /// exception (default: <see cref="ExitException"/>) that
    /// unwinds the wasm stack back to the
    /// <see cref="Wacs.ComponentModel.Runtime.ComponentInstance.Invoke"/>
    /// caller.</para>
    /// </summary>
    public interface IExit
    {
        /// <summary>Exit with a result-shaped status. <paramref
        /// name="isErr"/> mirrors the WIT discriminator
        /// directly: false (disc=0) → Ok → exit code 0; true
        /// (disc=1) → Err → exit code 1. Reads opposite-of-
        /// expected at the type level (true = bad), but matches
        /// the canonical-ABI bool encoding where the wire i32
        /// value passes through as-is. Should not return —
        /// implementations typically throw
        /// <see cref="ExitException"/>.</summary>
        void Exit(bool isErr);

        /// <summary>Exit with an explicit u8 code. Same
        /// divergence contract as <see cref="Exit"/>.</summary>
        void ExitWithCode(byte statusCode);
    }
}
