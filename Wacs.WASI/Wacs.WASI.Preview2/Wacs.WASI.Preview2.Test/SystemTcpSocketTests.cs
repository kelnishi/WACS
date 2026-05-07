// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Sockets;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>Hermetic loopback-only tests for
    /// <see cref="SystemTcpSocket"/>. Each test binds to
    /// 127.0.0.1 with port=0 (ephemeral) so the OS picks a
    /// free port and concurrent test runs don't collide.
    /// Tests include explicit timeouts so a hang surfaces as
    /// a failure rather than the default xUnit collection
    /// timeout.</summary>
    public class SystemTcpSocketTests
    {
        // 127.0.0.1:0 — ipv4 loopback, ephemeral port.
        private static IpSocketAddress LoopbackAddr(ushort port)
            => new IpSocketAddress.IpSocketAddressIpv4(
                new Ipv4SocketAddress
                {
                    Port = port,
                    Address = (127, 0, 0, 1),
                });

        [Fact]
        public void SystemTcpCreateSocket_returns_Ok()
        {
            var factory = new SystemTcpCreateSocket();
            var r = factory.CreateTcpSocket(IpAddressFamily.Ipv4);
            Assert.True(r.IsOk);
            using var sock = (SystemTcpSocket)r.Ok;
            Assert.NotNull(sock);
        }

        [Fact]
        public void SystemTcpSocket_bind_to_ephemeral_port_yields_local_address()
        {
            using var sock = new SystemTcpSocket(IpAddressFamily.Ipv4);
            var net = new Network();

            var startR = sock.StartBind(net, LoopbackAddr(0));
            Assert.True(startR.IsOk, "start-bind should succeed");
            var finishR = sock.FinishBind();
            Assert.True(finishR.IsOk, "finish-bind should succeed");

            var localR = sock.LocalAddress();
            Assert.True(localR.IsOk);
            Assert.IsType<IpSocketAddress.IpSocketAddressIpv4>(localR.Ok);
            var v4 = ((IpSocketAddress.IpSocketAddressIpv4)localR.Ok).Value;
            Assert.True(v4.Port > 0,
                "ephemeral port should resolve to a non-zero value");
            Assert.Equal(((byte)127, (byte)0, (byte)0, (byte)1), v4.Address);
        }

        [Fact]
        public void SystemTcpSocket_invalid_state_rejects_finish_without_start()
        {
            using var sock = new SystemTcpSocket(IpAddressFamily.Ipv4);
            // FinishBind without StartBind → InvalidState.
            var r = sock.FinishBind();
            Assert.True(r.IsErr);
            Assert.Equal(ErrorCode.InvalidState, r.Err);
            // FinishConnect without StartConnect → InvalidState.
            var c = sock.FinishConnect();
            Assert.True(c.IsErr);
            Assert.Equal(ErrorCode.InvalidState, c.Err);
            // FinishListen without StartListen → InvalidState.
            var l = sock.FinishListen();
            Assert.True(l.IsErr);
            Assert.Equal(ErrorCode.InvalidState, l.Err);
        }

        [Fact]
        public void SystemTcpSocket_listen_accept_connect_round_trips_message()
        {
            using var server = new SystemTcpSocket(IpAddressFamily.Ipv4);
            var net = new Network();
            Assert.True(server.StartBind(net, LoopbackAddr(0)).IsOk);
            Assert.True(server.FinishBind().IsOk);
            Assert.True(server.StartListen().IsOk);
            Assert.True(server.FinishListen().IsOk);

            var v4 = ((IpSocketAddress.IpSocketAddressIpv4)
                server.LocalAddress().Ok).Value;
            ushort port = v4.Port;

            // Drive accept on a worker so the client's connect
            // can complete. Both sides timed out at 5 s.
            (TcpSocket, IInputStream, IOutputStream)? accepted = null;
            Exception? acceptError = null;
            var acceptThread = new Thread(() =>
            {
                try { accepted = server.Accept(); }
                catch (Exception e) { acceptError = e; }
            }) { IsBackground = true };
            acceptThread.Start();

            using var client = new SystemTcpSocket(IpAddressFamily.Ipv4);
            Assert.True(client.StartConnect(net,
                LoopbackAddr(port)).IsOk);
            var connR = client.FinishConnect();
            Assert.True(connR.IsOk, "client connect to accepting "
                + "server should succeed");

            Assert.True(acceptThread.Join(TimeSpan.FromSeconds(5)),
                "server accept should complete within 5 s");
            Assert.Null(acceptError);
            Assert.NotNull(accepted);

            var (serverSock, sIn, sOut) = accepted!.Value;
            using var _serverSock = (SystemTcpSocket)serverSock;
            using var _sIn  = (IDisposable)sIn;
            using var _sOut = (IDisposable)sOut;

            var (cIn, cOut) = connR.Ok;
            using var _cIn  = (IDisposable)cIn;
            using var _cOut = (IDisposable)cOut;

            // Round-trip: client → server.
            byte[] msg = Encoding.UTF8.GetBytes("hello, loopback");
            var w = cOut.BlockingWriteAndFlush(msg);
            Assert.True(w.IsOk, "client write should succeed");

            var read = sIn.BlockingRead((ulong)msg.Length);
            Assert.True(read.IsOk, "server read should succeed");
            Assert.Equal(msg, read.Ok);

            // Verify peer addresses.
            var localOnAccepted = serverSock.LocalAddress();
            Assert.True(localOnAccepted.IsOk);
            var remoteOnClient = client.RemoteAddress();
            Assert.True(remoteOnClient.IsOk);
            var rv4 = ((IpSocketAddress.IpSocketAddressIpv4)
                remoteOnClient.Ok).Value;
            Assert.Equal(port, rv4.Port);
            Assert.Equal(((byte)127, (byte)0, (byte)0, (byte)1),
                rv4.Address);
        }

        [Fact]
        public void SystemTcpSocket_shutdown_send_signals_eof_to_peer()
        {
            using var server = new SystemTcpSocket(IpAddressFamily.Ipv4);
            var net = new Network();
            Assert.True(server.StartBind(net, LoopbackAddr(0)).IsOk);
            Assert.True(server.FinishBind().IsOk);
            Assert.True(server.StartListen().IsOk);
            Assert.True(server.FinishListen().IsOk);
            ushort port = ((IpSocketAddress.IpSocketAddressIpv4)
                server.LocalAddress().Ok).Value.Port;

            (TcpSocket, IInputStream, IOutputStream)? accepted = null;
            var acceptThread = new Thread(
                () => accepted = server.Accept())
            { IsBackground = true };
            acceptThread.Start();

            using var client = new SystemTcpSocket(IpAddressFamily.Ipv4);
            Assert.True(client.StartConnect(net,
                LoopbackAddr(port)).IsOk);
            var connR = client.FinishConnect();
            Assert.True(connR.IsOk);

            Assert.True(acceptThread.Join(TimeSpan.FromSeconds(5)));
            var (sSock, sIn, _sOutUnused) = accepted!.Value;
            using var _sSock = (SystemTcpSocket)sSock;
            using var _sIn = (IDisposable)sIn;
            using var _sOutUsed = (IDisposable)_sOutUnused;
            var (_cIn, cOut) = connR.Ok;
            using var _cIn2 = (IDisposable)_cIn;
            using var _cOut = (IDisposable)cOut;

            // Write some data, then half-close the send side.
            byte[] msg = Encoding.UTF8.GetBytes("bye");
            Assert.True(cOut.BlockingWriteAndFlush(msg).IsOk);
            var sd = client.Shutdown(ShutdownType.Send);
            Assert.True(sd.IsOk, "shutdown(Send) should succeed");

            // Server reads the message, then a follow-up read
            // sees EOF as Err(StreamErrorClosed).
            var first = sIn.BlockingRead(64);
            Assert.True(first.IsOk);
            Assert.Equal(msg, first.Ok);
            var second = sIn.BlockingRead(64);
            Assert.True(second.IsErr,
                "post-shutdown read should surface EOF");
            Assert.IsType<StreamError.StreamErrorClosed>(second.Err);
        }

        [Fact]
        public void SystemTcpSocket_connect_to_refused_port_yields_error()
        {
            // Find a free port by binding + closing a listener;
            // immediately try to connect — the OS recycles the
            // port quickly, so the connect should be refused
            // (or, on a tiny race, time out — both surface as
            // an Err).
            int port;
            using (var probe = new Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp))
            {
                probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)probe.LocalEndPoint!).Port;
                // Close without listening — port is now unbound.
            }

            using var client = new SystemTcpSocket(IpAddressFamily.Ipv4);
            var net = new Network();

            var startR = client.StartConnect(net,
                LoopbackAddr((ushort)port));
            Assert.True(startR.IsOk);
            // Drive FinishConnect on a worker thread so the test
            // bounds the wait — loopback connection-refused is
            // immediate but be defensive against platform retries.
            var connectTask = Task.Run(() => client.FinishConnect());
            Assert.True(connectTask.Wait(TimeSpan.FromSeconds(10)),
                "loopback connect-refused should resolve "
                + "within 10 s");
            var finishR = connectTask.Result;
            Assert.True(finishR.IsErr,
                "connect to closed port should fail");
            // ConnectionRefused is the canonical loopback case,
            // but Unknown is acceptable too on platforms that
            // surface a different SocketError code.
            Assert.True(
                finishR.Err == ErrorCode.ConnectionRefused
                || finishR.Err == ErrorCode.Unknown
                || finishR.Err == ErrorCode.RemoteUnreachable,
                $"expected ConnectionRefused / Unknown / "
                + $"RemoteUnreachable, got {finishR.Err}");
        }
    }

}
