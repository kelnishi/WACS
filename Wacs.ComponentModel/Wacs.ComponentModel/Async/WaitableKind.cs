// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Discriminator carried alongside each entry in the
    /// <see cref="AsyncDispatcher"/>'s shared per-component handle
    /// store. The CM async ABI keeps all waitable handles in a
    /// single namespace; the kind tag tells the dispatcher which
    /// canon ops apply to a given handle without partitioning the
    /// integer space.
    ///
    /// <para>Adding a new waitable shape is a two-step change:
    /// extend this enum and add the matching typed facade on
    /// <see cref="AsyncDispatcher"/>. The shared storage doesn't
    /// otherwise care what kinds exist.</para>
    /// </summary>
    public enum WaitableKind
    {
        Task,
        Subtask,
        WaitableSet,
        Stream,
        Future,
        ErrorContext,
    }
}
