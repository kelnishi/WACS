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
    /// Host representation of <c>wasi:io/streams@0.2.x</c>'s
    /// <c>output-stream</c> resource. Subset of the WIT
    /// surface that ships in v0:
    /// <code>
    /// resource output-stream {
    ///     check-write: func() -&gt; result&lt;u64, stream-error&gt;;
    ///     write: func(contents: list&lt;u8&gt;) -&gt; result&lt;_, stream-error&gt;;
    ///     blocking-write-and-flush: func(contents: list&lt;u8&gt;) -&gt; result&lt;_, stream-error&gt;;
    ///     flush: func() -&gt; result&lt;_, stream-error&gt;;
    ///     blocking-flush: func() -&gt; result&lt;_, stream-error&gt;;
    ///     subscribe: func() -&gt; own&lt;pollable&gt;;
    /// }
    /// </code>
    ///
    /// <para>Always-Ok semantics in v0 — host methods return
    /// void / u64 directly; the wrapper writes Ok with the
    /// payload (or no payload). Throwing from a host method
    /// propagates back through wasm — the StreamError variant
    /// payload form (last-operation-failed / closed) is a
    /// follow-up.</para>
    ///
    /// <para>The default impl is "no-op" — guests wire it as
    /// stdout/stderr; subclasses override <see cref="Write"/>
    /// to actually consume the bytes.</para>
    /// </summary>
    [WasiResource("output-stream")]
    public class OutputStream : IDisposable
    {
        /// <summary>Maximum bytes the host can accept right
        /// now without blocking. Default: large constant —
        /// most non-network sinks are happy to take any
        /// reasonable buffer.</summary>
        [WasiStreamResult]
        public virtual ulong CheckWrite() => 65_536UL;

        /// <summary>Write <paramref name="contents"/> to the
        /// underlying sink. Default: discard.</summary>
        [WasiStreamResult]
        public virtual void Write(byte[] contents) { }

        /// <summary>Block until <paramref name="contents"/> is
        /// fully written + flushed. Default: same as
        /// <see cref="Write"/> for the no-op impl.</summary>
        [WasiStreamResult]
        public virtual void BlockingWriteAndFlush(byte[] contents) { }

        /// <summary>Trigger flush of any buffered bytes.
        /// Default: no-op.</summary>
        [WasiStreamResult]
        public virtual void Flush() { }

        /// <summary>Block until all buffered bytes are flushed.
        /// Default: no-op.</summary>
        [WasiStreamResult]
        public virtual void BlockingFlush() { }

        /// <summary>Subscribe to a pollable signaling when the
        /// next write won't block. Default: always-ready
        /// pollable.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }
}
