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
        // wasi:sockets/tcp@0.2.8 — tcp-socket resource. Bare
        // bool / enum returns skip the result wrapper; everything
        // else is result<X, error-code> with the Result-returning
        // host method dispatched through WriteResult* helpers.
        // start-bind / start-connect take an ip-socket-address
        // param flat-lowered to 12 i32 wire slots.
        private static void BindTcpSocket(WasmRuntime runtime,
            ResourceContext resources)
        {
            var socks = resources.Table<TcpSocket>();
            var nets = resources.Table<Network>();
            var pollables = resources.Table<Pollable>();
            var ins = resources.Table<InputStream>();
            var outs = resources.Table<OutputStream>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (TcpNs, "[resource-drop]tcp-socket"),
                (_, h) => socks.Drop(h));

            // address-family() -> ip-address-family — bare enum.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.address-family"),
                (_, h) => (int)((TcpSocket)socks.Get(h)).AddressFamily());

            // subscribe() -> own<pollable> — no result wrap.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.subscribe"),
                (_, h) => pollables.Allocate(
                    (Pollable)((TcpSocket)socks.Get(h)).Subscribe()));

            // is-listening() -> bool — bare bool.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.is-listening"),
                (_, h) => ((TcpSocket)socks.Get(h)).IsListening()
                    ? 1 : 0);

            // start-bind(net, addr) -> result<_, error-code>.
            // Wire: handle + netHandle + 12 addr slots + retArea
            //       = 15 ints; Action<ExecContext, int×15> uses
            //       16 type params — Action's exact cap.
            runtime.BindHostFunction<Action<ExecContext, int, int,
                int, int, int, int, int, int, int, int, int, int, int, int, int>>(
                (TcpNs, "[method]tcp-socket.start-bind"),
                (ctx, h, hNet, disc, s1, s2, s3, s4, s5, s6, s7, s8,
                    s9, s10, s11, retArea) =>
                {
                    var addr = DecodeIpSocketAddressFlat(disc,
                        s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11);
                    var r = ((TcpSocket)socks.Get(h)).StartBind(
                        (Network)nets.Get(hNet), addr);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // finish-bind() -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.finish-bind"),
                (ctx, h, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h)).FinishBind();
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // start-connect(net, addr) -> result<_, error-code>.
            // Same shape as start-bind.
            runtime.BindHostFunction<Action<ExecContext, int, int,
                int, int, int, int, int, int, int, int, int, int, int, int, int>>(
                (TcpNs, "[method]tcp-socket.start-connect"),
                (ctx, h, hNet, disc, s1, s2, s3, s4, s5, s6, s7, s8,
                    s9, s10, s11, retArea) =>
                {
                    var addr = DecodeIpSocketAddressFlat(disc,
                        s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11);
                    var r = ((TcpSocket)socks.Get(h)).StartConnect(
                        (Network)nets.Get(hNet), addr);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // finish-connect() -> result<(input, output), _>.
            // retArea = 12 bytes: outer disc + 3 pad + in@+4 + out@+8.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.finish-connect"),
                (ctx, h, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h)).FinishConnect();
                    var mem = ctx.Memory();
                    if (r.IsOk)
                    {
                        var (inS, outS) = r.Ok;
                        mem.AsSpan(retArea, 1)[0] = 0;
                        mem.AsSpan(retArea + 1, 1)[0] = 0;
                        mem.AsSpan(retArea + 2, 1)[0] = 0;
                        mem.AsSpan(retArea + 3, 1)[0] = 0;
                        MemoryWriter.WriteI32LE(mem, retArea + 4,
                            ins.Allocate((InputStream)inS));
                        MemoryWriter.WriteI32LE(mem, retArea + 8,
                            outs.Allocate((OutputStream)outS));
                        return;
                    }
                    WriteErrCode(mem, retArea, 12, r.Err);
                });

            // start-listen() -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.start-listen"),
                (ctx, h, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h)).StartListen();
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // finish-listen() -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.finish-listen"),
                (ctx, h, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h)).FinishListen();
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // accept() -> result<(tcp-socket, in, out), _>.
            // retArea = 16 bytes: outer disc + 3 pad + 3×i32 handles.
            // Calls the ITcpSocket-shaped overload (Result-returning);
            // the public-virtual concrete-typed Accept() is the
            // host extension point and the explicit-interface shim
            // wraps its tuple.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.accept"),
                (ctx, h, retArea) =>
                {
                    var r = ((ITcpSocket)socks.Get(h)).Accept();
                    var mem = ctx.Memory();
                    if (r.IsOk)
                    {
                        var (s, inS, outS) = r.Ok;
                        mem.AsSpan(retArea, 1)[0] = 0;
                        mem.AsSpan(retArea + 1, 1)[0] = 0;
                        mem.AsSpan(retArea + 2, 1)[0] = 0;
                        mem.AsSpan(retArea + 3, 1)[0] = 0;
                        MemoryWriter.WriteI32LE(mem, retArea + 4,
                            socks.Allocate((TcpSocket)s));
                        MemoryWriter.WriteI32LE(mem, retArea + 8,
                            ins.Allocate((InputStream)inS));
                        MemoryWriter.WriteI32LE(mem, retArea + 12,
                            outs.Allocate((OutputStream)outS));
                        return;
                    }
                    WriteErrCode(mem, retArea, 16, r.Err);
                });

            // shutdown(shutdown-type) -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.shutdown"),
                (ctx, h, how, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .Shutdown((ShutdownType)how);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // local-address() / remote-address() ->
            //   result<ip-socket-address, error-code>.
            // retArea = 36 bytes: outer disc + 3 pad + 32-byte
            // ip-socket-address at +4. Variant align is 4, so
            // result.align = 4 → result.size = 4 + 32 = 36.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.local-address"),
                (ctx, h, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h)).LocalAddress();
                    WriteResultIpSocketAddress(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.remote-address"),
                (ctx, h, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h)).RemoteAddress();
                    WriteResultIpSocketAddress(ctx.Memory(), retArea, r);
                });

            // -----------------------------------------------------
            //   getter / setter pairs for socket options
            // -----------------------------------------------------

            // set-listen-backlog-size(u64) -> result<_, _>.
            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-listen-backlog-size"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetListenBacklogSize((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // keep-alive-enabled() -> result<bool, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-enabled"),
                (ctx, h, retArea) =>
                    WriteResultBool(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveEnabled()));

            // set-keep-alive-enabled(bool) -> result<_, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-enabled"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetKeepAliveEnabled(value != 0);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // keep-alive-idle-time() -> result<u64, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-idle-time"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveIdleTime()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-idle-time"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetKeepAliveIdleTime((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-interval"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveInterval()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-interval"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetKeepAliveInterval((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-count"),
                (ctx, h, retArea) =>
                    WriteResultU32(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveCount()));

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-count"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetKeepAliveCount((uint)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // hop-limit / set-hop-limit (u8 wire-widened to i32).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.hop-limit"),
                (ctx, h, retArea) =>
                    WriteResultU8(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).HopLimit()));

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.set-hop-limit"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetHopLimit((byte)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.receive-buffer-size"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).ReceiveBufferSize()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-receive-buffer-size"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetReceiveBufferSize((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.send-buffer-size"),
                (ctx, h, retArea) =>
                    WriteResultU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).SendBufferSize()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-send-buffer-size"),
                (ctx, h, value, retArea) =>
                {
                    var r = ((TcpSocket)socks.Get(h))
                        .SetSendBufferSize((ulong)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });
        }
    }
}
