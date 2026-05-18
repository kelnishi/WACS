// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Backing storage for a Component Model <c>stream&lt;T&gt;</c>
    /// handle. Backed by a bounded <see cref="Channel{T}"/> so:
    ///
    /// <list type="bullet">
    ///   <item>The host can <c>await</c> reads /
    ///     <c>await foreach</c> elements via
    ///     <see cref="AsAsyncEnumerable"/> — natural
    ///     <see cref="IAsyncEnumerable{T}"/> consumption.</item>
    ///   <item>The wasm-side producer hits backpressure when the
    ///     channel is full (<c>backpressure.set</c> drives the
    ///     bound). Writes wait by default; <see cref="TryWrite"/>
    ///     surfaces the immediate-fail variant.</item>
    ///   <item>The dispatcher (Slice C) bridges wasm
    ///     <c>stream.write</c> / <c>stream.read</c> to these
    ///     calls — the wasm-side suspend through Stack Switching
    ///     when a write/read blocks rides on top of the channel's
    ///     async semantics.</item>
    /// </list>
    ///
    /// <para>Single-threaded by convention — wasm execution is
    /// single-threaded per component instance. The
    /// <c>BoundedChannelOptions</c> are set accordingly
    /// (<c>SingleReader = SingleWriter = true</c>).</para>
    /// </summary>
    public sealed class StreamBuffer<T>
    {
        private readonly Channel<T> _channel;

        /// <summary>Bound the channel was created with.</summary>
        public int Capacity { get; }

        public StreamBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    "Stream buffer capacity must be positive.");
            Capacity = capacity;
            _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }

        /// <summary>True iff the writer side called <see cref="Complete"/>.</summary>
        public bool IsCompleted => _channel.Reader.Completion.IsCompleted;

        /// <summary>Attempt non-blocking write; returns false when the buffer is full.</summary>
        public bool TryWrite(T value) => _channel.Writer.TryWrite(value);

        /// <summary>Wait until the buffer has space then write. Suspends when full.</summary>
        public ValueTask WriteAsync(T value, CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(value, cancellationToken);

        /// <summary>Attempt non-blocking read; returns false when the buffer is empty.</summary>
        public bool TryRead(out T value) => _channel.Reader.TryRead(out value!);

        /// <summary>Wait for an item to arrive. Suspends when empty.</summary>
        public ValueTask<T> ReadAsync(CancellationToken cancellationToken = default) =>
            _channel.Reader.ReadAsync(cancellationToken);

        /// <summary>
        /// Mark the stream complete. Pending reads observe a
        /// <see cref="ChannelClosedException"/> only after the
        /// already-buffered items drain.
        /// </summary>
        public bool Complete() => _channel.Writer.TryComplete();

        /// <summary>
        /// Host-side consumption surface. Iterating drains the
        /// buffer one element at a time, awaiting when empty,
        /// and exits naturally on <see cref="Complete"/>.
        /// </summary>
        public IAsyncEnumerable<T> AsAsyncEnumerable(
            CancellationToken cancellationToken = default) =>
            _channel.Reader.ReadAllAsync(cancellationToken);

        /// <summary>Reader exposing the underlying channel — for embedders
        /// that already accept a <see cref="ChannelReader{T}"/>.</summary>
        public ChannelReader<T> Reader => _channel.Reader;
    }
}
