// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Host representation of <c>wasi:io/streams@0.2.x</c>'s
    /// <c>input-stream</c> resource.
    /// <code>
    /// resource input-stream {
    ///     read: func(len: u64) -&gt; result&lt;list&lt;u8&gt;, stream-error&gt;;
    ///     blocking-read: func(len: u64) -&gt; result&lt;list&lt;u8&gt;, stream-error&gt;;
    ///     skip: func(len: u64) -&gt; result&lt;u64, stream-error&gt;;
    ///     blocking-skip: func(len: u64) -&gt; result&lt;u64, stream-error&gt;;
    ///     subscribe: func() -&gt; own&lt;pollable&gt;;
    /// }
    /// </code>
    ///
    /// <para>Default impl returns empty bytes (EOF) — useful
    /// as the no-input stand-in for stdin in headless
    /// scenarios. Subclasses override <see cref="Read"/> to
    /// pull from a real source. Always-Ok semantics in v0;
    /// throwing propagates as wasm trap.</para>
    /// </summary>
    [WasiResource("input-stream")]
    public class InputStream : IInputStream, IDisposable
    {
        /// <summary>Read up to <paramref name="len"/> bytes
        /// from the stream. Empty array signals EOF (or no
        /// data ready in the non-blocking variant).</summary>
        public virtual byte[] Read(ulong len) => Array.Empty<byte>();

        /// <summary>Block until at least one byte is available,
        /// then read up to <paramref name="len"/>.</summary>
        public virtual byte[] BlockingRead(ulong len) => Array.Empty<byte>();

        /// <summary>Discard up to <paramref name="len"/> bytes
        /// without copying. Returns count actually skipped.</summary>
        public virtual ulong Skip(ulong len) => 0UL;

        public virtual ulong BlockingSkip(ulong len) => 0UL;

        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }

        // ----- IInputStream (faithful Result-returning shape) -----
        // Phase B4 shortcut: the hand-written InputStream uses a
        // throw-on-error idiom (the binding catches and encodes
        // Err). The generated IInputStream returns
        // Result<T, StreamError> directly; reconciling requires
        // refactoring every concrete subclass — deferred. The
        // explicit-interface stubs throw NotImplementedException
        // so any caller that does invoke them via the interface
        // surface gets a clear error. The CliBindings layer
        // continues calling the virtual non-Result methods above.
        Result<byte[], StreamError> IInputStream.Read(ulong len) =>
            throw new NotImplementedException(
                "InputStream uses throw-on-error idiom; bindings call Read(ulong) directly.");

        Result<byte[], StreamError> IInputStream.BlockingRead(ulong len) =>
            throw new NotImplementedException(
                "InputStream uses throw-on-error idiom; bindings call BlockingRead(ulong) directly.");

        Result<ulong, StreamError> IInputStream.Skip(ulong len) =>
            throw new NotImplementedException(
                "InputStream uses throw-on-error idiom; bindings call Skip(ulong) directly.");

        Result<ulong, StreamError> IInputStream.BlockingSkip(ulong len) =>
            throw new NotImplementedException(
                "InputStream uses throw-on-error idiom; bindings call BlockingSkip(ulong) directly.");

        IPollable IInputStream.Subscribe() => Subscribe();
    }
}
