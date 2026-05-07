// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Exceptions
{
    /// <summary>
    /// Trap kind produced when a wasm thread is interrupted via a CancellationToken
    /// or <see cref="Wacs.Core.Runtime.Concurrency.WasmThread.RequestTrap"/>. Subclass of
    /// <see cref="TrapException"/> so existing trap-handling paths propagate it
    /// naturally through to <c>WasmThread.Completion</c>.
    /// </summary>
    public class InterruptedException : TrapException
    {
        public InterruptedException(string reason) : base($"interrupted: {reason}")
        {
        }
    }
}
