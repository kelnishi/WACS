// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Filesystem;
using Wacs.WASI.Preview3.Sockets;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice Q coverage: the stream-shape host bindings
    /// for descriptor read/write/append-via-stream and
    /// tcp-socket send/receive. End-to-end I/O through the
    /// canon-async stream + future handles.
    /// </summary>
    public class StreamShapeBindingTests : IDisposable
    {
        private readonly string _tempRoot;

        public StreamShapeBindingTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-stream-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }

        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        // ---- Wire registrations --------------------------------------

        [Fact]
        public void BindToRuntime_registers_stream_shape_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string fs = WasiPreview3Host.FilesystemTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (fs, "[method]descriptor.read-via-stream"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (fs, "[method]descriptor.write-via-stream"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (fs, "[method]descriptor.append-via-stream"), out _));

            string sock = WasiPreview3Host.SocketsTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (sock, "[method]tcp-socket.send"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (sock, "[method]tcp-socket.receive"), out _));
        }

        // ---- descriptor.read-via-stream ------------------------------

        [Fact]
        public async Task InvokeDescriptorReadViaStream_yields_file_bytes_through_stream_handle()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var name = "read.bin";
            var content = Encoding.UTF8.GetBytes("read-via-stream-payload");
            await File.WriteAllBytesAsync(
                Path.Combine(_tempRoot, name), content);

            var desc = new Descriptor(
                Path.Combine(_tempRoot, name), _tempRoot,
                DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);

            const int retptr = 64;
            host.InvokeDescriptorReadViaStream(handle, 0, retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 0, 4));
            int futureHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(streamHandle > 0);
            Assert.True(futureHandle > 0);

            // Wait for the background read loop to drain the file
            // and complete the future. The future's Task tracks
            // the loop's exit.
            await dispatcher.FutureReadAsync(futureHandle);

            // Drain the stream buffer and confirm contents.
            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            var collected = new List<byte>();
            while (buf!.Reader.TryRead(out var b)) collected.Add(b);
            Assert.Equal(content, collected.ToArray());
        }

        [Fact]
        public void InvokeDescriptorReadViaStream_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            File.WriteAllText(
                Path.Combine(_tempRoot, "x.txt"), "x");
            var desc = new Descriptor(
                Path.Combine(_tempRoot, "x.txt"), _tempRoot,
                DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeDescriptorReadViaStream(
                    handle, 0, /* misaligned */ 5));
            Assert.Contains("misaligned", ex.Message);
        }

        // ---- descriptor.write-via-stream -----------------------------

        [Fact]
        public async Task InvokeDescriptorWriteViaStream_persists_stream_bytes_to_file()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var name = "write.bin";
            var desc = new Descriptor(
                Path.Combine(_tempRoot, name), _tempRoot,
                DescriptorFlags.Write);
            var handle = host.DescriptorHandles.Allocate(desc);

            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            int futureHandle = host.InvokeDescriptorWriteViaStream(
                handle, streamHandle, offset: 0);
            Assert.True(futureHandle > 0);

            // Guest writes bytes then closes the writable half.
            var content = Encoding.UTF8.GetBytes("WRITE-DATA");
            foreach (var b in content)
                dispatcher.StreamTryWrite(streamHandle, b);
            dispatcher.StreamDropWritable(streamHandle);
            dispatcher.StreamDropReadable(streamHandle);

            await dispatcher.FutureReadAsync(futureHandle);
            Assert.Equal(content,
                File.ReadAllBytes(Path.Combine(_tempRoot, name)));
        }

        [Fact]
        public async Task InvokeDescriptorAppendViaStream_extends_file_at_eof()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var name = "append.bin";
            File.WriteAllBytes(
                Path.Combine(_tempRoot, name),
                Encoding.UTF8.GetBytes("HEAD-"));

            var desc = new Descriptor(
                Path.Combine(_tempRoot, name), _tempRoot,
                DescriptorFlags.Write);
            var handle = host.DescriptorHandles.Allocate(desc);

            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            int futureHandle = host.InvokeDescriptorAppendViaStream(
                handle, streamHandle);

            var appended = Encoding.UTF8.GetBytes("TAIL");
            foreach (var b in appended)
                dispatcher.StreamTryWrite(streamHandle, b);
            dispatcher.StreamDropWritable(streamHandle);
            dispatcher.StreamDropReadable(streamHandle);

            await dispatcher.FutureReadAsync(futureHandle);
            Assert.Equal("HEAD-TAIL",
                File.ReadAllText(Path.Combine(_tempRoot, name)));
        }

        // ---- tcp-socket.send + receive (loopback round-trip) --------

        [Fact]
        public async Task TcpSocket_send_receive_round_trip_via_loopback()
        {
            // Build a synthetic .NET Socket listener on a free
            // loopback port, then drive the WACS TcpSocket
            // through connect / send / receive against it. This
            // exercises the full Send/Receive stream-handle wire
            // path end-to-end.
            using var listener = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(backlog: 1);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            // Accept on the server side: echo bytes back to the
            // client, then close.
            var serverTask = Task.Run(async () =>
            {
                using var srvClient = await listener.AcceptAsync();
                var buf = new byte[64];
                int n = await srvClient.ReceiveAsync(
                    new ArraySegment<byte>(buf), SocketFlags.None);
                // Echo + extra byte so client can distinguish
                // server-side data.
                var reply = new byte[n + 1];
                Buffer.BlockCopy(buf, 0, reply, 0, n);
                reply[n] = 0xFF;
                await srvClient.SendAsync(
                    new ArraySegment<byte>(reply), SocketFlags.None);
                srvClient.Shutdown(SocketShutdown.Send);
            });

            var dispatcher = new AsyncDispatcher();
            using var clientSocket = new TcpSocket(IpAddressFamily.Ipv4);
            await clientSocket.ConnectAsync(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(
                    (ushort)port, Ipv4Address.Loopback)));
            Assert.Equal(TcpSocket.State.Connected,
                clientSocket.CurrentState);

            // Send path: stage 4 bytes in a stream handle, drive
            // TcpSocket.Send, await the completion future.
            var sendStream = dispatcher.StreamNew(typeIdx: 0);
            var (sendFuture, sendCompletion) =
                clientSocket.Send(dispatcher, sendStream);
            foreach (var b in new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
                dispatcher.StreamTryWrite(sendStream, b);
            dispatcher.StreamDropWritable(sendStream);
            dispatcher.StreamDropReadable(sendStream);
            await sendCompletion;
            await dispatcher.FutureReadAsync(sendFuture);

            // Receive path: drive TcpSocket.Receive, await
            // the future, drain the stream.
            var (recvStream, recvFuture, recvCompletion) =
                clientSocket.Receive(dispatcher);
            await recvCompletion;
            await dispatcher.FutureReadAsync(recvFuture);

            var buf2 = dispatcher.GetByteStreamBuffer(recvStream);
            Assert.NotNull(buf2);
            var got = new List<byte>();
            while (buf2!.Reader.TryRead(out var b)) got.Add(b);
            Assert.Equal(
                new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xFF },
                got.ToArray());

            await serverTask;
        }

        [Fact]
        public void TcpSocket_send_unconnected_throws_invalid_state()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var dispatcher = new AsyncDispatcher();
            var ex = Assert.Throws<SocketsException>(
                () => sock.Send(dispatcher, 0));
            Assert.Equal(Sockets.ErrorCode.InvalidState, ex.Code);
        }

        [Fact]
        public void TcpSocket_receive_unconnected_throws_invalid_state()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var dispatcher = new AsyncDispatcher();
            var ex = Assert.Throws<SocketsException>(
                () => sock.Receive(dispatcher));
            Assert.Equal(Sockets.ErrorCode.InvalidState, ex.Code);
        }

        // ---- Wire-up integration for tcp-socket.send/receive --------

        [Fact]
        public async Task InvokeTcpSocketReceive_writes_handle_pair_at_retptr()
        {
            // Loopback handshake again, but this time we drive
            // the receive via InvokeTcpSocketReceive (the host
            // function delegate body) and confirm the (stream,
            // future) pair lands at retptr.
            using var listener = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var serverTask = Task.Run(async () =>
            {
                using var c = await listener.AcceptAsync();
                await c.SendAsync(new ArraySegment<byte>(
                    new byte[] { 0x42 }), SocketFlags.None);
                c.Shutdown(SocketShutdown.Send);
            });

            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            await sock.ConnectAsync(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(
                    (ushort)port, Ipv4Address.Loopback)));
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketReceive(handle, retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 0, 4));
            int futureHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(streamHandle > 0);
            Assert.True(futureHandle > 0);

            await dispatcher.FutureReadAsync(futureHandle);
            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            Assert.True(buf!.Reader.TryRead(out var b));
            Assert.Equal(0x42, b);

            await serverTask;
            sock.Dispose();
        }
    }
}
