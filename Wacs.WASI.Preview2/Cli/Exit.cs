// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Default <see cref="IExit"/> implementation — throws
    /// <see cref="ExitException"/> for both methods. Class name
    /// is <c>ExitHandler</c> rather than <c>Exit</c> to avoid
    /// the C# constructor-name clash with the
    /// <see cref="IExit.Exit"/> method. Hosts that want to
    /// integrate with their environment's exit semantics
    /// (e.g. <c>System.Environment.Exit</c>) should substitute
    /// their own <see cref="IExit"/> impl.
    /// </summary>
    public sealed class ExitHandler : IExit
    {
        /// <summary>Exit with a result-shaped status. Ok →
        /// exit code 0; Err → exit code 1. The faithful WIT
        /// mapping <c>result&lt;_, _&gt;</c> →
        /// <see cref="Result{Unit, Unit}"/> preserves the
        /// 2-case discriminant the canonical ABI lowers to a
        /// single i32.</summary>
        public void Exit(Result<Unit, Unit> status)
        {
            throw new ExitException(status.IsOk ? (byte)0 : (byte)1);
        }

        public void ExitWithCode(byte statusCode)
        {
            throw new ExitException(statusCode);
        }
    }
}
