// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Host representation of <c>wasi:io/poll@0.2.x</c>'s
    /// <c>pollable</c> resource.
    /// <code>
    /// interface poll {
    ///     resource pollable {
    ///         ready: func() -&gt; bool;
    ///         block: func();
    ///     }
    /// }
    /// </code>
    ///
    /// <para>A pollable represents some asynchronous condition
    /// (a stream becoming ready, a timer firing, etc.). Hosts
    /// providing pollables — typically as the return type of
    /// other interfaces' methods like
    /// <c>monotonic-clock.subscribe-instant</c> — implement
    /// the conditions internally.</para>
    ///
    /// <para>This base class is the simplest "always-ready"
    /// pollable: <see cref="Ready"/> returns true immediately,
    /// <see cref="Block"/> returns immediately. Useful as a
    /// stub for guests that just want to verify the wiring;
    /// real implementations subclass + override.</para>
    /// </summary>
    [WasiResource("pollable")]
    public class Pollable : IDisposable
    {
        /// <summary>True iff the underlying condition has
        /// already been triggered. Default: always ready.
        /// Subclasses override for conditions that are
        /// genuinely asynchronous.</summary>
        public virtual bool Ready() => true;

        /// <summary>Block (synchronously) until the underlying
        /// condition is met. Default: returns immediately.
        /// Real implementations should integrate with the
        /// host's async machinery — but synchronous block is
        /// a valid degraded mode for guests that don't care
        /// about throughput.</summary>
        public virtual void Block() { }

        public virtual void Dispose() { }
    }
}
