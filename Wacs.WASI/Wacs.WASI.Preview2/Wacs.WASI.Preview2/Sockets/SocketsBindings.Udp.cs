// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    public sealed partial class SocketsBindings
    {
        // wasi:sockets/udp@0.2.8 — udp-socket resource. Smaller
        // surface than tcp; %stream returns a (incoming, outgoing)
        // datagram-stream pair gated on an option<address>.
        private static void BindUdpSocket(WasmRuntime runtime,
            ResourceContext resources)
        {
            var socks = resources.Table<UdpSocket>();
            var nets = resources.Table<Network>();
            var pollables = resources.Table<Pollable>();
            var ins = resources.Table<IncomingDatagramStream>();
            var outs = resources.Table<OutgoingDatagramStream>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (UdpNs, "[resource-drop]udp-socket"),
                (_, h) => socks.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.address-family"),
                (_, h) => (int)((UdpSocket)socks.Get(h)).AddressFamily());

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.subscribe"),
                (_, h) => pollables.Allocate(
                    (Pollable)((UdpSocket)socks.Get(h)).Subscribe()));

            // start-bind(net, addr) -> result<_, error-code>
            // Same 16-arg shape as tcp-socket.start-bind.
            runtime.BindHostFunction<Action<ExecContext, int, int,
                int, int, int, int, int, int, int, int, int, int, int, int, int>>(
                (UdpNs, "[method]udp-socket.start-bind"),
                (ctx, h, hNet, disc, s1, s2, s3, s4, s5, s6, s7, s8,
                    s9, s10, s11, retArea) =>
                {
                    var addr = DecodeIpSocketAddressFlat(disc,
                        s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11);
                    var r = ((UdpSocket)socks.Get(h)).StartBind(
                        (Network)nets.Get(hNet), addr);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.finish-bind"),
                (ctx, h, retArea) =>
                {
                    var r = ((UdpSocket)socks.Get(h)).FinishBind();
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // %stream(option<addr>) ->
            //   result<(incoming, outgoing), error-code>.
            // Wire: handle + 13 option-flat slots + retArea = 15
            // ints; Action<ExecContext, int×15> = 16 type params.
            // retArea = 12 bytes: outer disc + 3 pad + in@+4 + out@+8.
            // Calls the IUdpSocket.Stream overload (Result-returning);
            // the public-virtual concrete-typed UdpStream is the
            // host extension point and the explicit-interface shim
            // wraps its tuple.
            runtime.BindHostFunction<Action<ExecContext, int, int,
                int, int, int, int, int, int, int, int, int, int, int, int, int>>(
                (UdpNs, "[method]udp-socket.stream"),
                (ctx, h, optDisc, vDisc, s1, s2, s3, s4, s5, s6, s7,
                    s8, s9, s10, s11, retArea) =>
                {
                    Option<IpSocketAddress> opt = optDisc == 0
                        ? Option<IpSocketAddress>.None
                        : Option<IpSocketAddress>.Some(
                            DecodeIpSocketAddressFlat(vDisc, s1, s2, s3,
                                s4, s5, s6, s7, s8, s9, s10, s11));
                    var r = ((IUdpSocket)socks.Get(h)).Stream(opt);
                    var mem = ctx.Memory();
                    if (r.IsOk)
                    {
                        var (inS, outS) = r.Ok;
                        mem[retArea] = 0;
                        mem[retArea + 1] = 0;
                        mem[retArea + 2] = 0;
                        mem[retArea + 3] = 0;
                        MemoryWriter.WriteI32LE(mem, retArea + 4,
                            ins.Allocate((IncomingDatagramStream)inS));
                        MemoryWriter.WriteI32LE(mem, retArea + 8,
                            outs.Allocate((OutgoingDatagramStream)outS));
                        return;
                    }
                    WriteErrCode(mem, retArea, 12, r.Err);
                });

            // local / remote address (same 36-byte shape as tcp).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.local-address"),
                (ctx, h, retArea) =>
                {
                    var r = ((UdpSocket)socks.Get(h)).LocalAddress();
                    WriteResultIpSocketAddress(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.remote-address"),
                (ctx, h, retArea) =>
                {
                    var r = ((UdpSocket)socks.Get(h)).RemoteAddress();
                    WriteResultIpSocketAddress(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.unicast-hop-limit"),
                (ctx, h, retArea) =>
                    WriteResultU8(ctx.Memory(), retArea,
                        ((UdpSocket)socks.Get(h)).UnicastHopLimit()));

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (UdpNs, "[method]udp-socket.set-unicast-hop-limit"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((UdpSocket)socks.Get(h))
                        .SetUnicastHopLimit((byte)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.receive-buffer-size"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((UdpSocket)socks.Get(h)).ReceiveBufferSize()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (UdpNs, "[method]udp-socket.set-receive-buffer-size"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((UdpSocket)socks.Get(h))
                        .SetReceiveBufferSize((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]udp-socket.send-buffer-size"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((UdpSocket)socks.Get(h)).SendBufferSize()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (UdpNs, "[method]udp-socket.set-send-buffer-size"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((UdpSocket)socks.Get(h))
                        .SetSendBufferSize((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });
        }

        // wasi:sockets/udp@0.2.8 — incoming-datagram-stream
        // resource. receive(maxResults) -> result<list<dgram>, _>
        // where each dgram = 40 bytes (data list 8B + addr 32B).
        private static void BindIncomingDatagramStream(WasmRuntime runtime,
            ResourceContext resources)
        {
            var streams = resources.Table<IncomingDatagramStream>();
            var pollables = resources.Table<Pollable>();
            var alloc = new Realloc(runtime);

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (UdpNs, "[resource-drop]incoming-datagram-stream"),
                (_, h) => streams.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (UdpNs, "[method]incoming-datagram-stream.subscribe"),
                (_, h) => pollables.Allocate(
                    (Pollable)((IncomingDatagramStream)streams.Get(h))
                        .Subscribe()));

            // receive(maxResults: u64) -> result<list<incoming-datagram>, _>
            // retArea = 12 bytes: outer disc + 3 pad + listPtr@+4 +
            // listLen@+8.
            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (UdpNs, "[method]incoming-datagram-stream.receive"),
                (ctx, h, maxResults, retArea) =>
                {
                    var r = ((IncomingDatagramStream)streams.Get(h))
                        .Receive((ulong)maxResults);
                    var mem = ctx.Memory();
                    if (!r.IsOk)
                    {
                        WriteErrCode(mem, retArea, 12, r.Err);
                        return;
                    }
                    var dgs = r.Ok;
                    int count = dgs.Length;
                    int arrayPtr = count == 0 ? 0
                        : alloc.Allocate(4, count * 40);
                    for (int i = 0; i < count; i++)
                    {
                        int elemBase = arrayPtr + i * 40;
                        var dg = dgs[i];
                        int dataPtr = dg.Data.Length == 0 ? 0
                            : alloc.Allocate(1, dg.Data.Length);
                        var memBuf = ctx.Memory();
                        if (dg.Data.Length > 0)
                            Array.Copy(dg.Data, 0, memBuf, dataPtr,
                                dg.Data.Length);
                        MemoryWriter.WriteI32LE(memBuf, elemBase, dataPtr);
                        MemoryWriter.WriteI32LE(memBuf, elemBase + 4,
                            dg.Data.Length);
                        // remote-address: 32-byte ip-socket-address
                        // record at elemBase+8.
                        WriteIpSocketAddress(memBuf, elemBase + 8,
                            dg.RemoteAddress);
                    }
                    mem = ctx.Memory();
                    mem[retArea] = 0;
                    mem[retArea + 1] = 0;
                    mem[retArea + 2] = 0;
                    mem[retArea + 3] = 0;
                    MemoryWriter.WriteI32LE(mem, retArea + 4, arrayPtr);
                    MemoryWriter.WriteI32LE(mem, retArea + 8, count);
                });
        }

        // wasi:sockets/udp@0.2.8 — outgoing-datagram-stream
        // resource. send(list<dgram>) -> result<u64, _>; each
        // dgram on the param side is 44 bytes.
        private static void BindOutgoingDatagramStream(WasmRuntime runtime,
            ResourceContext resources)
        {
            var streams = resources.Table<OutgoingDatagramStream>();
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (UdpNs, "[resource-drop]outgoing-datagram-stream"),
                (_, h) => streams.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (UdpNs, "[method]outgoing-datagram-stream.subscribe"),
                (_, h) => pollables.Allocate(
                    (Pollable)((OutgoingDatagramStream)streams.Get(h))
                        .Subscribe()));

            // check-send() -> result<u64, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpNs, "[method]outgoing-datagram-stream.check-send"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((OutgoingDatagramStream)streams.Get(h))
                            .CheckSend()));

            // send(list<outgoing-datagram>) -> result<u64, _>.
            // Each elem = 44 bytes: data list 8B + option<addr> 36B.
            //   option<addr> at elemBase+8: opt disc@+8, var disc@+12,
            //   var payload@+16..+43.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (UdpNs, "[method]outgoing-datagram-stream.send"),
                (ctx, h, listPtr, listLen, retArea) =>
                {
                    var memory = ctx.Memory();
                    var dgs = new OutgoingDatagram[listLen];
                    for (int i = 0; i < listLen; i++)
                    {
                        int elemBase = listPtr + i * 44;
                        int dataPtr = MemoryReader.ReadI32LE(
                            memory, elemBase);
                        int dataLen = MemoryReader.ReadI32LE(
                            memory, elemBase + 4);
                        var data = new byte[dataLen];
                        if (dataLen > 0)
                            Array.Copy(memory, dataPtr, data, 0, dataLen);
                        Option<IpSocketAddress> remote =
                            Option<IpSocketAddress>.None;
                        if (memory[elemBase + 8] != 0)
                        {
                            byte vDisc = memory[elemBase + 12];
                            if (vDisc == 0)
                            {
                                ushort port = (ushort)(memory[elemBase + 16]
                                    | (memory[elemBase + 17] << 8));
                                remote = Option<IpSocketAddress>.Some(
                                    new IpSocketAddress.IpSocketAddressIpv4(
                                        new Ipv4SocketAddress {
                                            Port = port,
                                            Address = (
                                                memory[elemBase + 18],
                                                memory[elemBase + 19],
                                                memory[elemBase + 20],
                                                memory[elemBase + 21]),
                                        }));
                            }
                            else
                            {
                                ushort port = (ushort)(memory[elemBase + 16]
                                    | (memory[elemBase + 17] << 8));
                                uint flow = (uint)MemoryReader.ReadI32LE(
                                    memory, elemBase + 20);
                                ushort A(int off) => (ushort)(memory[off]
                                    | (memory[off + 1] << 8));
                                uint scope = (uint)MemoryReader.ReadI32LE(
                                    memory, elemBase + 40);
                                remote = Option<IpSocketAddress>.Some(
                                    new IpSocketAddress.IpSocketAddressIpv6(
                                        new Ipv6SocketAddress {
                                            Port = port,
                                            FlowInfo = flow,
                                            Address = (
                                                A(elemBase + 24),
                                                A(elemBase + 26),
                                                A(elemBase + 28),
                                                A(elemBase + 30),
                                                A(elemBase + 32),
                                                A(elemBase + 34),
                                                A(elemBase + 36),
                                                A(elemBase + 38)),
                                            ScopeId = scope,
                                        }));
                            }
                        }
                        dgs[i] = new OutgoingDatagram {
                            Data = data,
                            RemoteAddress = remote,
                        };
                    }
                    var sendResult = ((OutgoingDatagramStream)streams.Get(h))
                        .Send(dgs);
                    WriteResultU64(ctx.Memory(), retArea, sendResult);
                });
        }
    }
}
