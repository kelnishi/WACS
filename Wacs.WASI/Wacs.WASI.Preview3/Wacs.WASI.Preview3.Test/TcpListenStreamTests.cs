// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Sockets;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice R coverage: <c>tcp-socket.listen</c>
    /// typed-stream return. The stream serializes each
    /// accepted socket's handle as 4 LE bytes — the canon-ABI
    /// wire form of <c>own&lt;tcp-socket&gt;</c>. Guests
    /// reading the stream get 4 bytes per accepted socket.
    /// </summary>
    public class TcpListenStreamTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class StubRealloc : ICabiRealloc
        {
            public int Allocate(int align, int size) =>
                throw new InvalidOperationException(
                    "StubRealloc not expected on the success path " +
                    "of this slice's tests.");
        }

        // Helper: bind a TcpSocket to a free loopback port.
        // Returns the bound socket + its assigned port so the
        // test can dial in from the .NET client side.
        private static (TcpSocket Socket, int Port) BindLoopback()
        {
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            int port = sock.GetLocalAddress().V4.Port;
            return (sock, port);
        }

        // ---- TcpSocket.Listen impl directly --------------------------

        [Fact]
        public async Task TcpSocket_listen_accepts_clients_through_stream_buffer()
        {
            // Bind a WACS listener, kick off Listen, then dial
            // it from a couple of .NET client sockets. Confirm
            // the stream buffer receives 4 LE bytes per
            // accepted socket and the host table has matching
            // handles.
            var dispatcher = new AsyncDispatcher();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var (listener, port) = BindLoopback();

            var streamHandle = listener.ListenInternal(dispatcher, host);
            Assert.Equal(TcpSocket.State.Listening, listener.CurrentState);
            Assert.True(streamHandle > 0);

            // Dial in from two .NET clients sequentially.
            using var c1 = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            await c1.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port));
            using var c2 = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            await c2.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port));

            // Wait for the accept loop to publish both handles
            // into the byte stream — 4 bytes per accepted socket,
            // so we need 8 bytes total.
            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            var collected = new System.Collections.Generic.List<byte>();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (collected.Count < 8 && DateTime.UtcNow < deadline)
            {
                if (buf!.Reader.TryRead(out var b)) collected.Add(b);
                else await Task.Delay(10);
            }
            Assert.Equal(8, collected.Count);

            // Deserialize each i32 handle from the LE byte stream.
            int h1 = BinaryPrimitives.ReadInt32LittleEndian(
                collected.ToArray().AsSpan(0, 4));
            int h2 = BinaryPrimitives.ReadInt32LittleEndian(
                collected.ToArray().AsSpan(4, 4));
            Assert.True(h1 > 0);
            Assert.True(h2 > 0);
            Assert.NotEqual(h1, h2);

            // Each handle resolves to a Connected TcpSocket in
            // the host table.
            var acc1 = host.TcpSocketHandles.Get(h1) as TcpSocket;
            var acc2 = host.TcpSocketHandles.Get(h2) as TcpSocket;
            Assert.NotNull(acc1);
            Assert.NotNull(acc2);
            Assert.Equal(TcpSocket.State.Connected, acc1!.CurrentState);
            Assert.Equal(TcpSocket.State.Connected, acc2!.CurrentState);

            listener.Dispose();
        }

        [Fact]
        public void TcpSocket_listen_unbound_throws_invalid_state()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var dispatcher = new AsyncDispatcher();
            var ex = Assert.Throws<SocketsException>(
                () => sock.Listen(dispatcher));
            Assert.Equal(Sockets.ErrorCode.InvalidState, ex.Code);
        }

        [Fact]
        public void TcpSocket_listen_already_listening_throws_invalid_state()
        {
            var dispatcher = new AsyncDispatcher();
            var (sock, _) = BindLoopback();
            sock.Listen(dispatcher);
            var ex = Assert.Throws<SocketsException>(
                () => sock.Listen(dispatcher));
            Assert.Equal(Sockets.ErrorCode.InvalidState, ex.Code);
            sock.Dispose();
        }

        // ---- Wire-up integration ------------------------------------

        [Fact]
        public void BindToRuntime_registers_tcp_socket_listen()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.SocketsTypesModuleName,
                 "[method]tcp-socket.listen"), out _));
        }

        [Fact]
        public async Task InvokeTcpSocketListen_writes_stream_handle_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var (listener, port) = BindLoopback();
            var handle = host.TcpSocketHandles.Allocate(listener);

            const int retptr = 64;
            host.InvokeTcpSocketListen(
                handle, retptr, new StubRealloc());

            // result-disc = 0 (ok) at +0
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // stream-handle (i32) at +4
            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(streamHandle > 0);

            // Sanity: one client connects, one accepted socket
            // ends up in the host table.
            using var c = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            await c.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port));

            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            var collected = new System.Collections.Generic.List<byte>();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (collected.Count < 4 && DateTime.UtcNow < deadline)
            {
                if (buf!.Reader.TryRead(out var b)) collected.Add(b);
                else await Task.Delay(10);
            }
            Assert.Equal(4, collected.Count);

            int acceptedHandle = BinaryPrimitives.ReadInt32LittleEndian(
                collected.ToArray());
            Assert.NotNull(host.TcpSocketHandles.Get(acceptedHandle));

            listener.Dispose();
        }

        [Fact]
        public void InvokeTcpSocketListen_unbound_writes_err_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            using var unbound = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(unbound);

            const int retptr = 64;
            host.InvokeTcpSocketListen(
                handle, retptr, new StubRealloc());

            // result-disc = 1 (err)
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // error-code = InvalidState (5 in enum order)
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public async Task TcpSocket_listen_to_send_receive_full_loop()
        {
            // The full server-side flow: bind, listen, accept,
            // read the accepted handle, send + receive on it.
            // Exercises the typed-stream return THEN the bytes
            // streams from Slice Q against the accepted socket.
            var dispatcher = new AsyncDispatcher();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var (listener, port) = BindLoopback();
            var listenStreamHandle = listener.ListenInternal(
                dispatcher, host);

            // Client dials in, sends a byte, half-closes its
            // send side.
            using var client = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port));
            await client.SendAsync(
                new ArraySegment<byte>(new byte[] { 0x33 }),
                SocketFlags.None);
            client.Shutdown(SocketShutdown.Send);

            // Pull the accepted-handle bytes off the listen stream.
            var listenBuf = dispatcher.GetByteStreamBuffer(
                listenStreamHandle);
            Assert.NotNull(listenBuf);
            var bytes = new byte[4];
            var deadline = DateTime.UtcNow.AddSeconds(2);
            int n = 0;
            while (n < 4 && DateTime.UtcNow < deadline)
            {
                if (listenBuf!.Reader.TryRead(out var b))
                    bytes[n++] = b;
                else await Task.Delay(10);
            }
            int acceptedHandle = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            var accepted = host.TcpSocketHandles.Get(acceptedHandle)
                as TcpSocket;
            Assert.NotNull(accepted);

            // Receive the byte on the accepted socket.
            var (recvStream, recvFuture, recvCompletion) =
                accepted!.Receive(dispatcher);
            await recvCompletion;
            await dispatcher.FutureReadAsync(recvFuture);
            var recvBuf = dispatcher.GetByteStreamBuffer(recvStream);
            Assert.NotNull(recvBuf);
            Assert.True(recvBuf!.Reader.TryRead(out var got));
            Assert.Equal(0x33, got);

            listener.Dispose();
            accepted.Dispose();
        }
    }
}
