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
    /// Host representation of <c>wasi:io/error@0.2.x</c>'s
    /// <c>error</c> resource.
    /// <code>
    /// interface error {
    ///     resource error {
    ///         to-debug-string: func() -&gt; string;
    ///     }
    /// }
    /// </code>
    ///
    /// <para>An <c>error</c> represents an opaque, host-defined
    /// failure condition — typically returned from streams /
    /// filesystem / sockets methods that fail. The guest can
    /// only inspect it via <see cref="ToDebugString"/> for
    /// human-facing logging; the spec deliberately doesn't
    /// expose machine-readable structure to keep error
    /// taxonomy a host concern.</para>
    /// </summary>
    [WasiResource("error")]
    public class Error : IDisposable
    {
        /// <summary>The debug string the guest sees. Set at
        /// construction; subclasses can override
        /// <see cref="ToDebugString"/> for dynamic content.</summary>
        public string Message { get; }

        public Error(string message)
        {
            Message = message ?? "";
        }

        public virtual string ToDebugString() => Message;

        public virtual void Dispose() { }
    }
}
