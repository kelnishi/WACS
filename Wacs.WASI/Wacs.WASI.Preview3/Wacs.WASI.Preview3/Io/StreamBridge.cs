// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;

namespace Wacs.WASI.Preview3.Io
{
    /// <summary>
    /// Adapter helpers that bridge a canon-async
    /// <see cref="StreamBuffer{T}"/> to a host
    /// <see cref="System.IO.Stream"/> backing.
    ///
    /// <para>Direction depends on which side of the canon
    /// <c>stream&lt;u8&gt;</c> is host-facing:</para>
    /// <list type="bullet">
    ///   <item><see cref="DrainToStream"/>: guest writes into
    ///     the buffer (e.g. <c>cli/stdout.write-via-stream</c>);
    ///     host drains into a sink <c>Stream</c>.</item>
    ///   <item><see cref="PumpFromStream"/>: host writes from a
    ///     source <c>Stream</c> into the buffer (e.g.
    ///     <c>cli/stdin.read-via-stream</c>); guest reads it
    ///     out.</item>
    /// </list>
    ///
    /// <para>Both helpers run on a CLR background task —
    /// returns a <see cref="Task"/> the caller can compose
    /// against the canon-async future the dispatcher exposes
    /// for the operation's result.</para>
    /// </summary>
    public static class StreamBridge
    {
        /// <summary>
        /// Drain bytes from the buffer into <paramref name="sink"/>
        /// until the buffer's writer side closes. Flushes the
        /// sink before returning.
        /// </summary>
        public static Task DrainToStream(
            StreamBuffer<byte> buffer, Stream sink,
            CancellationToken cancellationToken = default)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            return Task.Run(async () =>
            {
                await foreach (var b in
                    buffer.AsAsyncEnumerable(cancellationToken)
                        .ConfigureAwait(false))
                {
                    sink.WriteByte(b);
                }
                await sink.FlushAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// Pump bytes from <paramref name="source"/> into the
        /// buffer until the source is exhausted, then close the
        /// buffer's writer side.
        /// </summary>
        public static Task PumpFromStream(
            Stream source, StreamBuffer<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return Task.Run(async () =>
            {
                try
                {
                    var staging = new byte[4096];
                    while (true)
                    {
                        var n = await source.ReadAsync(
                            staging, 0, staging.Length,
                            cancellationToken).ConfigureAwait(false);
                        if (n == 0) break;
                        for (int i = 0; i < n; i++)
                        {
                            await buffer.WriteAsync(
                                staging[i], cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    buffer.Complete();
                }
            }, cancellationToken);
        }
    }
}
