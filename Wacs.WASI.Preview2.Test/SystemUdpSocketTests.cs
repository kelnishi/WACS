// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Sockets;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>Hermetic loopback-only tests for
    /// <see cref="SystemUdpSocket"/>. Each test binds to
    /// 127.0.0.1 with port=0 (ephemeral) so the OS picks a
    /// free port and concurrent test runs don't collide.
    /// Tests include explicit timeouts so a hang surfaces as
    /// a failure rather than the default xUnit collection
    /// timeout.</summary>
    public class SystemUdpSocketTests
    {
        // 127.0.0.1:port — ipv4 loopback.
        private static IpSocketAddress LoopbackAddr(ushort port)
            => new IpSocketAddress.IpSocketAddressIpv4(
                new Ipv4SocketAddress
                {
                    Port = port,
                    Address = (127, 0, 0, 1),
                });

        [Fact]
        public void SystemUdpCreateSocket_returns_Ok()
        {
            var factory = new SystemUdpCreateSocket();
            var r = factory.CreateUdpSocket(IpAddressFamily.Ipv4);
            Assert.True(r.IsOk);
            using var sock = (SystemUdpSocket)r.Ok;
            Assert.NotNull(sock);
        }

        [Fact]
        public void SystemUdpSocket_bind_to_ephemeral_port_yields_local_address()
        {
            using var sock = new SystemUdpSocket(IpAddressFamily.Ipv4);
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
        public void SystemUdpSocket_invalid_state_rejects_finish_without_start()
        {
            using var sock = new SystemUdpSocket(IpAddressFamily.Ipv4);
            var r = sock.FinishBind();
            Assert.True(r.IsErr);
            Assert.Equal(ErrorCode.InvalidState, r.Err);
        }

        [Fact]
        public void SystemUdpSocket_self_send_round_trips_message()
        {
            // One socket: bind, then send a datagram to itself.
            // Loopback delivers it back into the same socket's
            // receive queue, where Receive picks it up.
            using var sock = new SystemUdpSocket(IpAddressFamily.Ipv4);
            var net = new Network();
            Assert.True(sock.StartBind(net, LoopbackAddr(0)).IsOk);
            Assert.True(sock.FinishBind().IsOk);

            var local = sock.LocalAddress();
            Assert.True(local.IsOk);
            var lv4 = ((IpSocketAddress.IpSocketAddressIpv4)local.Ok).Value;
            ushort port = lv4.Port;

            // Yield (incoming, outgoing) pair, unpinned —
            // per-datagram remote-address is required.
            var (inS, outS) = sock.UdpStream(null);
            using var _inS = (IDisposable)inS;
            using var _outS = (IDisposable)outS;

            byte[] msg = Encoding.UTF8.GetBytes("hello, udp");
            var dg = new OutgoingDatagram
            {
                Data = msg,
                RemoteAddress = Option<IpSocketAddress>.Some(
                    LoopbackAddr(port)),
            };
            var sendR = outS.Send(new[] { dg });
            Assert.True(sendR.IsOk, "send should succeed");
            Assert.Equal(1UL, sendR.Ok);

            // Wait briefly for the datagram to land in the
            // receive queue. Loopback delivery is fast but the
            // ReceiveFrom call uses Available > 0 polling, so
            // give the kernel a few ms. Bail out after 5 s.
            IncomingDatagram[]? received = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var r = inS.Receive(8);
                Assert.True(r.IsOk);
                if (r.Ok.Length > 0)
                {
                    received = r.Ok;
                    break;
                }
                Thread.Sleep(10);
            }
            Assert.NotNull(received);
            Assert.Single(received!);
            Assert.Equal(msg, received![0].Data);

            // The remote-address on the receive should be the
            // local port we sent from.
            var rv4 = ((IpSocketAddress.IpSocketAddressIpv4)
                received[0].RemoteAddress).Value;
            Assert.Equal(((byte)127, (byte)0, (byte)0, (byte)1),
                rv4.Address);
            Assert.Equal(port, rv4.Port);
        }

        [Fact]
        public void SystemUdpSocket_two_sockets_round_trip_message()
        {
            using var server = new SystemUdpSocket(IpAddressFamily.Ipv4);
            using var client = new SystemUdpSocket(IpAddressFamily.Ipv4);
            var net = new Network();

            Assert.True(server.StartBind(net, LoopbackAddr(0)).IsOk);
            Assert.True(server.FinishBind().IsOk);
            Assert.True(client.StartBind(net, LoopbackAddr(0)).IsOk);
            Assert.True(client.FinishBind().IsOk);

            ushort serverPort = ((IpSocketAddress.IpSocketAddressIpv4)
                server.LocalAddress().Ok).Value.Port;
            ushort clientPort = ((IpSocketAddress.IpSocketAddressIpv4)
                client.LocalAddress().Ok).Value.Port;

            var (sIn, _sOut) = server.UdpStream(null);
            using var _sInD = (IDisposable)sIn;
            using var _sOutD = (IDisposable)_sOut;
            var (_cIn, cOut) = client.UdpStream(null);
            using var _cInD = (IDisposable)_cIn;
            using var _cOutD = (IDisposable)cOut;

            byte[] msg = Encoding.UTF8.GetBytes("ping from client");
            var dg = new OutgoingDatagram
            {
                Data = msg,
                RemoteAddress = Option<IpSocketAddress>.Some(
                    LoopbackAddr(serverPort)),
            };
            var sendR = cOut.Send(new[] { dg });
            Assert.True(sendR.IsOk);
            Assert.Equal(1UL, sendR.Ok);

            IncomingDatagram[]? received = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var r = sIn.Receive(8);
                Assert.True(r.IsOk);
                if (r.Ok.Length > 0)
                {
                    received = r.Ok;
                    break;
                }
                Thread.Sleep(10);
            }
            Assert.NotNull(received);
            Assert.Single(received!);
            Assert.Equal(msg, received![0].Data);

            var rv4 = ((IpSocketAddress.IpSocketAddressIpv4)
                received[0].RemoteAddress).Value;
            Assert.Equal(((byte)127, (byte)0, (byte)0, (byte)1),
                rv4.Address);
            Assert.Equal(clientPort, rv4.Port);
        }

        [Fact]
        public void SystemUdpSocket_receive_with_no_pending_returns_empty()
        {
            using var sock = new SystemUdpSocket(IpAddressFamily.Ipv4);
            var net = new Network();
            Assert.True(sock.StartBind(net, LoopbackAddr(0)).IsOk);
            Assert.True(sock.FinishBind().IsOk);

            var (inS, _outS) = sock.UdpStream(null);
            using var _inSD = (IDisposable)inS;
            using var _outSD = (IDisposable)_outS;

            // Nothing was sent to this socket — Receive returns
            // Ok with an empty array (non-blocking semantics).
            var r = inS.Receive(8);
            Assert.True(r.IsOk);
            Assert.Empty(r.Ok);
        }
    }
}
