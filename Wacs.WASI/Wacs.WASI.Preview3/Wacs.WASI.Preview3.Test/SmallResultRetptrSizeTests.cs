// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading;
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
    /// Phase 5 Slice MM coverage: pin the corrected retptr sizes
    /// for result&lt;bool|u8|u32, error-code&gt; (20 bytes, was
    /// 12) and result&lt;list&lt;ip-address&gt;, error-code&gt;
    /// (20 bytes, was 16). The 12/16 sizes were derived from
    /// treating the error-code variant as 8/12 bytes (payload
    /// only) rather than 16 bytes (disc + padded payload).
    /// Simple-arm err paths still encoded correctly under the
    /// old sizes; the bug only manifested for the Other(option
    /// &lt;string&gt;) arm which needs the full 16-byte variant
    /// region for its ptr+len.
    /// </summary>
    public class SmallResultRetptrSizeTests
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

        // ---- result<bool, error-code> -------------------------------

        private sealed class ThrowingTcp : ITcpSocket
        {
            public SocketsException Throw;
            public ThrowingTcp(SocketsException ex) { Throw = ex; }
            public bool GetKeepAliveEnabled() => throw Throw;

            // Unused stubs for the rest of the interface
            public void Bind(IpSocketAddress a) { }
            public Task ConnectAsync(IpSocketAddress a,
                CancellationToken c = default) => Task.CompletedTask;
            public int Listen(AsyncDispatcher d) => 0;
            public (int futureHandle, Task SendCompletion)
                Send(AsyncDispatcher d, int sh)
                => (0, Task.CompletedTask);
            public (int streamHandle, int futureHandle, Task ReceiveCompletion)
                Receive(AsyncDispatcher d)
                => (0, 0, Task.CompletedTask);
            public IpSocketAddress GetLocalAddress() => default;
            public IpSocketAddress GetRemoteAddress() => default;
            public bool GetIsListening() => false;
            public IpAddressFamily GetAddressFamily() => IpAddressFamily.Ipv4;
            public void SetListenBacklogSize(ulong v) { }
            public void SetKeepAliveEnabled(bool v) { }
            public ulong GetKeepAliveIdleTime() => 0;
            public void SetKeepAliveIdleTime(ulong v) { }
            public ulong GetKeepAliveInterval() => 0;
            public void SetKeepAliveInterval(ulong v) { }
            public uint GetKeepAliveCount() => 0;
            public void SetKeepAliveCount(uint v) { }
            public byte GetHopLimit() => 0;
            public void SetHopLimit(byte v) { }
            public ulong GetReceiveBufferSize() => 0;
            public void SetReceiveBufferSize(ulong v) { }
            public ulong GetSendBufferSize() => 0;
            public void SetSendBufferSize(ulong v) { }
        }

        [Fact]
        public void ResultSmallErrorCode_other_arm_writes_full_option_string()
        {
            // Throw Other with a non-null OtherPayload. The option<string>
            // payload at the err arm needs ptr (i32) + len (i32)
            // at variant-offsets +4..+12 = retptr-offsets +8..+16.
            // Under the old 12-byte size the encoder truncated at
            // retptr+12, dropping the len bytes.
            var disp = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = disp };
            var stub = new ThrowingTcp(new SocketsException(
                Sockets.ErrorCode.Other, "the message body"));
            int handle = host.TcpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeTcpSocketGetterResultBool(
                handle, retptr, realloc, s => s.GetKeepAliveEnabled());

            // result-disc = 1 (err) at +0
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]);
            // error-code variant: disc=14 (Other) at +4, then
            // option<string> at variant +4..16 = retptr +8..+20:
            //   +8:  option-disc = 1 (some)
            //   +12: str-ptr (i32)
            //   +16: str-len (i32)
            Assert.Equal(14, disp.Memory.AsSpan(retptr + 4, 1)[0]);
            Assert.Equal(1, disp.Memory.AsSpan(retptr + 8, 1)[0]);
            int strPtr = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 12, 4));
            int strLen = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 16, 4));
            byte[] bytes = new byte[strLen];
            disp.Memory.AsSpan(strPtr, strLen).CopyTo(bytes);
            Assert.Equal("the message body", Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void ResultSmallErrorCode_size_is_20_not_12()
        {
            // The corrected retptr is 20 bytes 4-aligned. A
            // 20-byte allocation at the end of a 1-page memory
            // should pass validation. Under the old 12-byte
            // size the bound would have been retptr + 12 ≤
            // memory.Length, which would allow narrower
            // allocations at smaller offsets to pass — but
            // any 16-byte-or-narrower guest allocation would
            // get incorrect bytes written past its buffer.
            var disp = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = disp };
            var stub = new ThrowingTcp(new SocketsException(
                Sockets.ErrorCode.NotSupported));
            int handle = host.TcpSocketHandles.Allocate(stub);

            // 65536 - 20 = 65516 (4-aligned). Just fits at 20
            // bytes; 12 would have allowed 65524 — under the
            // new validator 65524 still passes since 65524 +
            // 20 = 65544 > 65536 → reject. So 65524 specifically
            // is the case where the new validator rejects what
            // the old (buggy) validator would accept.
            const int retptr20 = 65536 - 20;
            host.InvokeTcpSocketGetterResultBool(
                handle, retptr20, new SequentialRealloc(1024),
                s => s.GetKeepAliveEnabled());
            Assert.Equal(1, disp.Memory.AsSpan(retptr20, 1)[0]);

            const int retptrTooClose = 65536 - 16;
            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeTcpSocketGetterResultBool(
                    handle, retptrTooClose,
                    new SequentialRealloc(1024),
                    s => s.GetKeepAliveEnabled()));
            Assert.Contains("misaligned", ex.Message);
            // Actually 65536-16=65520, which is 4-aligned, so
            // the failure is from the size check, not align.
            // The error message uses the joint "misaligned or
            // out of range" phrasing.
        }

        // ---- result<list<ip-address>, error-code> -------------------

        private sealed class ThrowingNameLookup : IIpNameLookup
        {
            public Task<System.Collections.Generic.IReadOnlyList<IpAddress>>
                ResolveAddressesAsync(string name,
                    CancellationToken cancellationToken = default)
                => Task.FromException<System.Collections.Generic.IReadOnlyList<IpAddress>>(
                    new SocketsException(
                        Sockets.ErrorCode.Other, "resolver explosion"));
        }

        [Fact]
        public void ResolveAddresses_other_arm_writes_full_option_string()
        {
            // Same shape as the bool test above but for the
            // ip-name-lookup variant via resolve-addresses.
            var disp = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    IpNameLookup = new ThrowingNameLookup(),
                })
            { Dispatcher = disp };
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeResolveAddresses(0, 0, retptr, realloc);

            // result-disc=1, error-code disc=5 (Other in
            // ip-name-lookup), option<string> at +8..20.
            Assert.Equal(1, disp.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(5, disp.Memory.AsSpan(retptr + 4, 1)[0]);
            Assert.Equal(1, disp.Memory.AsSpan(retptr + 8, 1)[0]);
            int strPtr = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 12, 4));
            int strLen = BinaryPrimitives.ReadInt32LittleEndian(
                disp.Memory.AsSpan(retptr + 16, 4));
            byte[] bytes = new byte[strLen];
            disp.Memory.AsSpan(strPtr, strLen).CopyTo(bytes);
            Assert.Equal("resolver explosion",
                Encoding.UTF8.GetString(bytes));
        }
    }
}
