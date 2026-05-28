// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
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
    /// Phase 5 Slice X coverage: the remaining flat tcp-socket
    /// getter/setter methods (is-listening, listen-backlog-size,
    /// keep-alive family, hop-limit) plus udp-socket.disconnect.
    /// Exercises the new 12-byte 4-aligned result<bool/u8/u32,
    /// error-code> retptr encodings and the shared setter
    /// dispatch.
    /// </summary>
    public class TcpSocketSimpleOptionsTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class SequentialRealloc : ICabiRealloc
        {
            private int _next;
            public SequentialRealloc(int baseAddr) { _next = baseAddr; }
            public int Allocate(int align, int size)
            {
                _next = (_next + (align - 1)) & ~(align - 1);
                int ptr = _next;
                _next += size;
                return ptr;
            }
        }

        [Fact]
        public void BindToRuntime_registers_simple_tcp_options()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.SocketsTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-is-listening"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-listen-backlog-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-keep-alive-enabled"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-keep-alive-enabled"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-keep-alive-idle-time"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-keep-alive-idle-time"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-keep-alive-interval"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-keep-alive-interval"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-keep-alive-count"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-keep-alive-count"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-hop-limit"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-hop-limit"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.disconnect"), out _));
        }

        // ---- get-is-listening ---------------------------------------

        [Fact]
        public void TcpSocket_get_is_listening_returns_zero_when_unbound()
        {
            var host = new WasiPreview3Host();
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);

            // Direct impl call — the wire path is a Func that
            // returns the bool as i32 directly.
            Assert.False(host.RequireTcpSocket(handle).GetIsListening());
        }

        // ---- result<bool, error-code> encoding ----------------------
        //
        // Layout: 12 bytes 4-aligned.
        //   +0:    result-disc (u8) + 3 pad
        //   +4:    bool value (u8 0/1) OR error-code-disc (u8) + 7 pad
        //
        // The TCP keep-alive-enabled getter is functional on the
        // default System.Net.Sockets backing, so it round-trips
        // through the wire path cleanly.

        [Fact]
        public void TcpSocket_get_keep_alive_enabled_writes_ok_disc()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketGetterResultBool(
                handle, retptr, realloc, s => s.GetKeepAliveEnabled());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // bool value at +4 — either 0 or 1, both valid.
            byte v = dispatcher.Memory.AsSpan(retptr + 4, 1)[0];
            Assert.True(v == 0 || v == 1);
        }

        [Fact]
        public void TcpSocket_get_keep_alive_idle_time_writes_not_supported()
        {
            // GetKeepAliveIdleTime throws NotSupported on the
            // default backing. Pins err-path encoding on the
            // 24-byte 8-aligned u64 retptr.
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketGetterResultU64(
                handle, retptr, realloc, s => s.GetKeepAliveIdleTime());

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // error-code at +8 (align-8 payload offset)
            Assert.Equal((byte)Sockets.ErrorCode.NotSupported,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        [Fact]
        public void TcpSocket_get_hop_limit_writes_value_at_offset_4()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            // Set first so we know what to expect back.
            socket.SetHopLimit(42);

            const int retptr = 64;
            host.InvokeTcpSocketGetterResultU8(
                handle, retptr, realloc, s => s.GetHopLimit());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(42, dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void TcpSocket_get_keep_alive_count_writes_not_supported_via_u32()
        {
            // Default backing throws NotSupported for the count
            // getter — exercises the u32 result-shape's err path.
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketGetterResultU32(
                handle, retptr, realloc, s => s.GetKeepAliveCount());

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.NotSupported,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- setter dispatch (result<_, error-code>) ----------------

        [Fact]
        public void TcpSocket_set_listen_backlog_size_writes_ok()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketSetterErrorCode(
                handle, retptr, realloc,
                s => s.SetListenBacklogSize(128));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
        }

        [Fact]
        public void TcpSocket_set_keep_alive_enabled_round_trips()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int setterRetptr = 64;
            host.InvokeTcpSocketSetterErrorCode(
                handle, setterRetptr, realloc,
                s => s.SetKeepAliveEnabled(true));
            Assert.Equal(0,
                dispatcher.Memory.AsSpan(setterRetptr, 1)[0]);

            const int getterRetptr = 128;
            host.InvokeTcpSocketGetterResultBool(
                handle, getterRetptr, realloc,
                s => s.GetKeepAliveEnabled());
            Assert.Equal(0,
                dispatcher.Memory.AsSpan(getterRetptr, 1)[0]);
            Assert.Equal(1,
                dispatcher.Memory.AsSpan(getterRetptr + 4, 1)[0]);
        }

        [Fact]
        public void TcpSocket_set_keep_alive_idle_time_writes_not_supported()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketSetterErrorCode(
                handle, retptr, realloc,
                s => s.SetKeepAliveIdleTime(1_000_000_000UL));

            // 20-byte sockets-error-code retptr (Slice O shape):
            //   disc=1 at +0, error-code at +4
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.NotSupported,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- u32 keep-alive-count setter ----------------------------

        [Fact]
        public void TcpSocket_set_keep_alive_count_writes_not_supported_setter()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketSetterErrorCode(
                handle, retptr, realloc,
                s => s.SetKeepAliveCount(7));
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.NotSupported,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- UDP disconnect -----------------------------------------

        [Fact]
        public void UdpSocket_disconnect_unconnected_writes_invalid_state()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new UdpSocket(IpAddressFamily.Ipv4);
            int handle = host.UdpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeUdpSocketSetterErrorCode(
                handle, retptr, realloc,
                s => s.Disconnect());

            // Default UdpSocket.Disconnect throws InvalidState on
            // an unconnected socket — pins err-path encoding on
            // the shared UDP setter dispatch.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- retptr alignment ---------------------------------------

        [Fact]
        public void GetterResultBool_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var socket = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = host.TcpSocketHandles.Allocate(socket);
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeTcpSocketGetterResultBool(
                    handle, /* not 4-aligned */ 13,
                    realloc, s => s.GetKeepAliveEnabled()));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
