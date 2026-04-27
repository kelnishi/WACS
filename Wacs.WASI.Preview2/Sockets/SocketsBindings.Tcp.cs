// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    public sealed partial class SocketsBindings
    {
        // wasi:sockets/tcp@0.2.3 — tcp-socket resource. Bare
        // bool / enum returns skip the result wrapper; everything
        // else uses result<X, error-code>. start-bind and
        // start-connect take an ip-socket-address param flat-
        // lowered to 12 i32 wire slots.
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
                    ((TcpSocket)socks.Get(h)).Subscribe()));

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
                    ((TcpSocket)socks.Get(h)).StartBind(
                        (Network)nets.Get(hNet), addr);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // finish-bind() -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.finish-bind"),
                (ctx, h, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).FinishBind();
                    WriteOkUnit(ctx.Memory(), retArea);
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
                    ((TcpSocket)socks.Get(h)).StartConnect(
                        (Network)nets.Get(hNet), addr);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // finish-connect() -> result<(input, output), _>.
            // retArea = 12 bytes: disc + 3 pad + in@+4 + out@+8.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.finish-connect"),
                (ctx, h, retArea) =>
                {
                    var (inS, outS) = ((TcpSocket)socks.Get(h))
                        .FinishConnect();
                    var mem = ctx.Memory();
                    mem[retArea] = 0;
                    mem[retArea + 1] = 0;
                    mem[retArea + 2] = 0;
                    mem[retArea + 3] = 0;
                    MemoryWriter.WriteI32LE(mem, retArea + 4,
                        ins.Allocate(inS));
                    MemoryWriter.WriteI32LE(mem, retArea + 8,
                        outs.Allocate(outS));
                });

            // start-listen() -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.start-listen"),
                (ctx, h, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).StartListen();
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // finish-listen() -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.finish-listen"),
                (ctx, h, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).FinishListen();
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // accept() -> result<(tcp-socket, in, out), _>.
            // retArea = 16 bytes: disc + 3 pad + 3×i32 handles.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.accept"),
                (ctx, h, retArea) =>
                {
                    var (s, inS, outS) = ((TcpSocket)socks.Get(h)).Accept();
                    var mem = ctx.Memory();
                    mem[retArea] = 0;
                    mem[retArea + 1] = 0;
                    mem[retArea + 2] = 0;
                    mem[retArea + 3] = 0;
                    MemoryWriter.WriteI32LE(mem, retArea + 4,
                        socks.Allocate(s));
                    MemoryWriter.WriteI32LE(mem, retArea + 8,
                        ins.Allocate(inS));
                    MemoryWriter.WriteI32LE(mem, retArea + 12,
                        outs.Allocate(outS));
                });

            // shutdown(shutdown-type) -> result<_, error-code>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.shutdown"),
                (ctx, h, how, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).Shutdown((ShutdownType)how);
                    WriteOkUnit(ctx.Memory(), retArea);
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
                    var addr = ((TcpSocket)socks.Get(h)).LocalAddress();
                    var mem = ctx.Memory();
                    mem[retArea] = 0;
                    mem[retArea + 1] = 0;
                    mem[retArea + 2] = 0;
                    mem[retArea + 3] = 0;
                    WriteIpSocketAddress(mem, retArea + 4, addr);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.remote-address"),
                (ctx, h, retArea) =>
                {
                    var addr = ((TcpSocket)socks.Get(h)).RemoteAddress();
                    var mem = ctx.Memory();
                    mem[retArea] = 0;
                    mem[retArea + 1] = 0;
                    mem[retArea + 2] = 0;
                    mem[retArea + 3] = 0;
                    WriteIpSocketAddress(mem, retArea + 4, addr);
                });

            // -----------------------------------------------------
            //   getter / setter pairs for socket options
            // -----------------------------------------------------

            // set-listen-backlog-size(u64) -> result<_, _>.
            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-listen-backlog-size"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetListenBacklogSize(
                        (ulong)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // keep-alive-enabled() -> result<bool, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-enabled"),
                (ctx, h, retArea) =>
                    WriteOkBool(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveEnabled()));

            // set-keep-alive-enabled(bool) -> result<_, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-enabled"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetKeepAliveEnabled(
                        value != 0);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // keep-alive-idle-time() -> result<u64, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-idle-time"),
                (ctx, h, retArea) =>
                    WriteOkU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveIdleTime()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-idle-time"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetKeepAliveIdleTime(
                        (ulong)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-interval"),
                (ctx, h, retArea) =>
                    WriteOkU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveInterval()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-interval"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetKeepAliveInterval(
                        (ulong)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.keep-alive-count"),
                (ctx, h, retArea) =>
                    WriteOkU32(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).KeepAliveCount()));

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.set-keep-alive-count"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetKeepAliveCount(
                        (uint)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // hop-limit / set-hop-limit (u8 wire-widened to i32).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.hop-limit"),
                (ctx, h, retArea) =>
                    WriteOkU8(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).HopLimit()));

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (TcpNs, "[method]tcp-socket.set-hop-limit"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetHopLimit((byte)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.receive-buffer-size"),
                (ctx, h, retArea) =>
                    WriteOkU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).ReceiveBufferSize()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-receive-buffer-size"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetReceiveBufferSize(
                        (ulong)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpNs, "[method]tcp-socket.send-buffer-size"),
                (ctx, h, retArea) =>
                    WriteOkU64(ctx.Memory(), retArea,
                        ((TcpSocket)socks.Get(h)).SendBufferSize()));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (TcpNs, "[method]tcp-socket.set-send-buffer-size"),
                (ctx, h, value, retArea) =>
                {
                    ((TcpSocket)socks.Get(h)).SetSendBufferSize(
                        (ulong)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });
        }
    }
}
