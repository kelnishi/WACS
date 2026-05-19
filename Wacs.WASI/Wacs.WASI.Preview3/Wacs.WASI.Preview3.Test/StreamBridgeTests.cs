// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.WASI.Preview3.Io;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Exercises the <see cref="StreamBridge"/> helpers — the
    /// only piece of <c>WACS.WASI.Preview3</c> v0 that has full
    /// CLR-side semantics testable without a real wit-component
    /// fixture. The Cli/* host implementations rely on the
    /// same bridge; pinning its behavior here protects them
    /// transitively.
    /// </summary>
    public class StreamBridgeTests
    {
        [Fact]
        public async Task DrainToStream_writes_buffered_bytes_to_sink_in_order()
        {
            var buf = new StreamBuffer<byte>(capacity: 16);
            buf.TryWrite(0x68); // 'h'
            buf.TryWrite(0x69); // 'i'
            buf.Complete();

            using var sink = new MemoryStream();
            await StreamBridge.DrainToStream(buf, sink);

            Assert.Equal("hi", Encoding.UTF8.GetString(sink.ToArray()));
        }

        [Fact]
        public async Task PumpFromStream_drains_source_into_buffer_and_completes()
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes("ok"));
            var buf = new StreamBuffer<byte>(capacity: 8);

            await StreamBridge.PumpFromStream(source, buf);

            // Buffer's writer closed; reader sees the bytes
            // then end-of-stream.
            Assert.True(buf.TryRead(out var a)); Assert.Equal(0x6F, a); // 'o'
            Assert.True(buf.TryRead(out var b)); Assert.Equal(0x6B, b); // 'k'
            Assert.False(buf.TryRead(out _));
            Assert.True(buf.IsCompleted);
        }

        [Fact]
        public async Task DrainToStream_handles_large_payload_chunked()
        {
            // Larger-than-internal-staging payload to verify
            // the loop doesn't bail early.
            var payload = new byte[8192];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);
            var buf = new StreamBuffer<byte>(capacity: 16384);
            foreach (var b in payload) buf.TryWrite(b);
            buf.Complete();

            using var sink = new MemoryStream();
            await StreamBridge.DrainToStream(buf, sink);

            Assert.Equal(payload, sink.ToArray());
        }

        [Fact]
        public void DrainToStream_rejects_null_args()
        {
            // Argument validation is synchronous; void-returning
            // local functions disambiguate xunit's Action vs
            // Func<Task> overload selection.
            var buf = new StreamBuffer<byte>(4);
            using var sink = new MemoryStream();
            void NullBuf() => StreamBridge.DrainToStream(null!, sink);
            void NullSink() => StreamBridge.DrainToStream(buf, null!);
            Assert.Throws<ArgumentNullException>(NullBuf);
            Assert.Throws<ArgumentNullException>(NullSink);
        }

        [Fact]
        public void PumpFromStream_rejects_null_args()
        {
            var buf = new StreamBuffer<byte>(4);
            using var source = new MemoryStream();
            void NullSource() => StreamBridge.PumpFromStream(null!, buf);
            void NullBuf() => StreamBridge.PumpFromStream(source, null!);
            Assert.Throws<ArgumentNullException>(NullSource);
            Assert.Throws<ArgumentNullException>(NullBuf);
        }
    }
}
