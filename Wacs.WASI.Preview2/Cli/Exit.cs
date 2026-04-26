// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

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
        public void Exit(bool isErr)
        {
            throw new ExitException(isErr ? (byte)1 : (byte)0);
        }

        public void ExitWithCode(byte statusCode)
        {
            throw new ExitException(statusCode);
        }
    }
}
