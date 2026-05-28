// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;

namespace Wacs.WASI.Preview3.Cli
{
    /// <summary>
    /// Host implementation of <c>wasi:cli/stdin@0.3.0-rc-2026-03-15</c>.
    /// The interface's single op is:
    /// <code>
    /// read-via-stream: func()
    ///     -> tuple&lt;stream&lt;u8&gt;, future&lt;result&lt;_, error-code&gt;&gt;&gt;;
    /// </code>
    /// Returns a stream the host writes into (from the
    /// underlying <see cref="Stream"/> backing) and a future the
    /// host completes once reading finishes.
    /// </summary>
    public interface IStdin
    {
        /// <summary>
        /// Allocate a <c>stream&lt;u8&gt;</c> + future pair and
        /// kick off the host's read loop. Returns the stream
        /// handle (writable side held by host, readable side
        /// surfaced to guest), the future handle for the read
        /// result, and a CLR <see cref="Task"/> that completes
        /// when the host-side read finishes.
        /// </summary>
        (int streamHandle, int futureHandle, Task ReadCompletion)
            ReadViaStream(AsyncDispatcher dispatcher);
    }

    /// <summary>
    /// Host implementation of <c>wasi:cli/stdout</c>:
    /// <code>
    /// write-via-stream: func(data: stream&lt;u8&gt;)
    ///     -> future&lt;result&lt;_, error-code&gt;&gt;;
    /// </code>
    /// The host receives a stream handle the guest writes into
    /// and drains it to the underlying <see cref="Stream"/>
    /// backing (real stdout, capture buffer, etc.). Returns a
    /// future handle the host completes once the guest's
    /// writable side closes.
    /// </summary>
    public interface IStdout
    {
        (int futureHandle, Task WriteCompletion) WriteViaStream(
            AsyncDispatcher dispatcher, int streamHandle);
    }

    /// <summary>Same shape as <see cref="IStdout"/> for stderr.</summary>
    public interface IStderr
    {
        (int futureHandle, Task WriteCompletion) WriteViaStream(
            AsyncDispatcher dispatcher, int streamHandle);
    }

    /// <summary>
    /// Default <see cref="IStdin"/> backed by a host
    /// <see cref="Stream"/> — typically
    /// <see cref="Console.OpenStandardInput"/>. Drains the
    /// underlying stream into a <see cref="StreamBuffer{T}"/>
    /// on a background task; the guest reads from the buffer.
    /// </summary>
    public sealed class StreamBackedStdin : IStdin
    {
        private readonly Stream _source;

        public StreamBackedStdin(Stream source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public (int streamHandle, int futureHandle, Task ReadCompletion)
            ReadViaStream(AsyncDispatcher dispatcher)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            var completion = Task.Run(async () =>
            {
                try
                {
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var n = await _source.ReadAsync(
                            buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (n == 0) break;
                        for (int i = 0; i < n; i++)
                            dispatcher.StreamTryWrite(streamHandle, buffer[i]);
                    }
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle, /* ok */ null);
                }
                catch (Exception ex)
                {
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle, ex);
                }
            });
            return (streamHandle, futureHandle, completion);
        }
    }

    /// <summary>
    /// Default <see cref="IStdout"/>/<see cref="IStderr"/>
    /// backed by a host <see cref="Stream"/> — typically
    /// <see cref="Console.OpenStandardOutput"/> /
    /// <see cref="Console.OpenStandardError"/>. Drains the
    /// dispatcher's buffer into the sink on a background task.
    /// </summary>
    public sealed class StreamBackedSink : IStdout, IStderr
    {
        private readonly Stream _sink;

        public StreamBackedSink(Stream sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public (int futureHandle, Task WriteCompletion) WriteViaStream(
            AsyncDispatcher dispatcher, int streamHandle)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            // Capture the buffer reference at bind time so the
            // drain loop doesn't have to re-resolve through the
            // dispatcher's handle table per iteration. The
            // buffer ALSO outlives the StreamSlot's release —
            // ChannelReader.WaitToReadAsync surfaces channel
            // completion as the read-loop exit signal, so we
            // drain through end-of-stream cleanly.
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            if (buffer == null)
            {
                dispatcher.FutureWrite(futureHandle,
                    new InvalidOperationException(
                        $"stream handle {streamHandle} not allocated"));
                return (futureHandle, Task.CompletedTask);
            }

            var completion = Task.Run(async () =>
            {
                try
                {
                    var staging = new byte[4096];
                    while (await buffer.Reader
                        .WaitToReadAsync().ConfigureAwait(false))
                    {
                        int n = 0;
                        while (n < staging.Length
                               && buffer.Reader.TryRead(out var b))
                        {
                            staging[n++] = b;
                        }
                        if (n == 0) continue;
                        await _sink.WriteAsync(staging, 0, n)
                            .ConfigureAwait(false);
                    }
                    await _sink.FlushAsync().ConfigureAwait(false);
                    dispatcher.FutureWrite(futureHandle, /* ok */ null);
                }
                catch (Exception ex)
                {
                    dispatcher.FutureWrite(futureHandle, ex);
                }
            });
            return (futureHandle, completion);
        }
    }
}
