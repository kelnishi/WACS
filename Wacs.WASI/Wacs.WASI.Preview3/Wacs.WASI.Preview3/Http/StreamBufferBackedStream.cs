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

namespace Wacs.WASI.Preview3.Http
{
    /// <summary>
    /// <see cref="System.IO.Stream"/> adapter that reads bytes
    /// from a <see cref="StreamBuffer{T}"/>. Bridges the
    /// component-model <c>stream&lt;u8&gt;</c> handle (held by
    /// the dispatcher) into the host-side
    /// <see cref="IRequest.Body"/> / <see cref="IResponse.Body"/>
    /// contract: HTTP backends that read the Body see the bytes
    /// the wasm guest writes via <c>canon stream.write</c>.
    ///
    /// <para>Backpressure: <see cref="Read(byte[], int, int)"/>
    /// blocks until at least one byte is available or the
    /// stream is completed. EOF is signaled by the underlying
    /// buffer's <see cref="StreamBuffer{T}.Complete"/>.</para>
    ///
    /// <para>Write-side methods (<see cref="Write"/>,
    /// <see cref="WriteAsync"/>, <see cref="SetLength"/>,
    /// <see cref="Seek"/>) all throw
    /// <see cref="NotSupportedException"/> — this is a
    /// read-only stream from the host's perspective. The wasm
    /// guest produces bytes through the canon-ABI stream API,
    /// not this Stream object.</para>
    /// </summary>
    public sealed class StreamBufferBackedStream : Stream
    {
        private readonly StreamBuffer<byte> _buffer;

        public StreamBufferBackedStream(StreamBuffer<byte> buffer)
        {
            _buffer = buffer
                ?? throw new ArgumentNullException(nameof(buffer));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException(
            "StreamBufferBackedStream has no fixed length.");
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { /* read-only — no-op */ }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            // Block on the first byte; pull additional non-blocking
            // until the channel is empty or the destination is full.
            int read = 0;
            while (read < count)
            {
                if (_buffer.TryRead(out byte b))
                {
                    buffer[offset + read] = b;
                    read++;
                    continue;
                }
                if (read > 0) break;
                // First-byte path: block on the async read; on
                // ChannelClosed return 0 (EOF).
                try
                {
                    byte first = _buffer.ReadAsync()
                        .AsTask().GetAwaiter().GetResult();
                    buffer[offset + read] = first;
                    read++;
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return 0; // EOF
                }
            }
            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            if (count == 0) return 0;
            int read = 0;
            while (read < count)
            {
                if (_buffer.TryRead(out byte b))
                {
                    buffer[offset + read] = b;
                    read++;
                    continue;
                }
                if (read > 0) break;
                try
                {
                    byte first = await _buffer.ReadAsync(cancellationToken);
                    buffer[offset + read] = first;
                    read++;
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return 0;
                }
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value)
            => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException(
                "StreamBufferBackedStream is read-only.");
    }
}
