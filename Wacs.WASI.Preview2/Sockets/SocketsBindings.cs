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

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Orchestrator for the seven <c>wasi:sockets/*</c> WIT
    /// interfaces. Each interface binds independently and may
    /// be skipped by passing <c>null</c> for the corresponding
    /// host impl. Resource tables (Network, TcpSocket,
    /// UdpSocket, IncomingDatagramStream, OutgoingDatagramStream,
    /// ResolveAddressStream) always wire — guests typically
    /// receive resource handles from constructors elsewhere
    /// and the resource methods need to be reachable regardless.
    ///
    /// <para>WIT namespaces covered:
    /// <list type="bullet">
    /// <item><c>wasi:sockets/network@0.2.3</c> (Network resource)</item>
    /// <item><c>wasi:sockets/instance-network@0.2.3</c> (top-level)</item>
    /// <item><c>wasi:sockets/tcp@0.2.3</c> (TcpSocket resource)</item>
    /// <item><c>wasi:sockets/udp@0.2.3</c> (UdpSocket + datagram streams)</item>
    /// <item><c>wasi:sockets/tcp-create-socket@0.2.3</c></item>
    /// <item><c>wasi:sockets/udp-create-socket@0.2.3</c></item>
    /// <item><c>wasi:sockets/ip-name-lookup@0.2.3</c></item>
    /// </list></para>
    /// </summary>
    public sealed partial class SocketsBindings : IBindable
    {
        private const string NetworkNs        = "wasi:sockets/network@0.2.3";
        private const string InstanceNs       = "wasi:sockets/instance-network@0.2.3";
        private const string TcpNs            = "wasi:sockets/tcp@0.2.3";
        private const string UdpNs            = "wasi:sockets/udp@0.2.3";
        private const string TcpCreateNs      = "wasi:sockets/tcp-create-socket@0.2.3";
        private const string UdpCreateNs      = "wasi:sockets/udp-create-socket@0.2.3";
        private const string IpNameLookupNs   = "wasi:sockets/ip-name-lookup@0.2.3";

        private readonly ResourceContext _resources;
        private readonly IInstanceNetwork? _instanceNetwork;
        private readonly ITcpCreateSocket? _tcpCreate;
        private readonly IUdpCreateSocket? _udpCreate;
        private readonly IIpNameLookup? _ipNameLookup;

        public SocketsBindings(ResourceContext resources,
            IInstanceNetwork? instanceNetwork = null,
            ITcpCreateSocket? tcpCreate = null,
            IUdpCreateSocket? udpCreate = null,
            IIpNameLookup? ipNameLookup = null)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _instanceNetwork = instanceNetwork;
            _tcpCreate = tcpCreate;
            _udpCreate = udpCreate;
            _ipNameLookup = ipNameLookup;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            BindNetwork(runtime, _resources);
            if (_instanceNetwork != null)
                BindInstanceNetwork(runtime, _resources, _instanceNetwork);
            if (_tcpCreate != null)
                BindTcpCreateSocket(runtime, _resources, _tcpCreate);
            if (_udpCreate != null)
                BindUdpCreateSocket(runtime, _resources, _udpCreate);
            BindTcpSocket(runtime, _resources);
            BindUdpSocket(runtime, _resources);
            BindIncomingDatagramStream(runtime, _resources);
            BindOutgoingDatagramStream(runtime, _resources);
            BindIpNameLookup(runtime, _resources, _ipNameLookup);
        }

        // -----------------------------------------------------
        //   shared retArea encoders for result<X, error-code>
        // -----------------------------------------------------
        // error-code is u8 align 1; result.align matches the
        // Ok-side payload's alignment.

        // result<_, error-code>: 1 byte. Pad bytes are caller's
        // responsibility when the variant alignment requires.
        private static void WriteOkUnit(byte[] mem, int retArea)
        {
            mem[retArea] = 0;
        }

        // result<bool, error-code>: 2 bytes (disc + bool).
        private static void WriteOkBool(byte[] mem, int retArea, bool value)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = value ? (byte)1 : (byte)0;
        }

        // result<u8, error-code>: 2 bytes (disc + u8).
        private static void WriteOkU8(byte[] mem, int retArea, byte value)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = value;
        }

        // result<u32, error-code>: 8 bytes (disc + 3 pad + u32).
        private static void WriteOkU32(byte[] mem, int retArea, uint value)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            MemoryWriter.WriteU32LE(mem, retArea + 4, value);
        }

        // result<u64, error-code>: 16 bytes (disc + 7 pad + u64).
        private static void WriteOkU64(byte[] mem, int retArea, ulong value)
        {
            mem[retArea] = 0;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 8, value);
        }

        // result<own<X>, error-code>: 8 bytes (disc + 3 pad + handle).
        private static void WriteOkHandle(byte[] mem, int retArea, int handle)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            MemoryWriter.WriteI32LE(mem, retArea + 4, handle);
        }

        // -----------------------------------------------------
        //   ip-socket-address variant — wire shape in/out
        // -----------------------------------------------------

        // Decode the flat-lowered ip-socket-address (12 wire
        // slots: 1 disc + 11 i32 payload). Disc 0 = ipv4
        // (uses s1=port, s2..s5=address bytes); disc 1 = ipv6
        // (s1=port, s2=flow, s3..s10=u16 groups, s11=scope).
        // Slots beyond what each case needs are ignored (the
        // canon ABI joins variants by max-aligned union).
        private static IpSocketAddress DecodeIpSocketAddressFlat(
            int disc, int s1, int s2, int s3, int s4, int s5,
            int s6, int s7, int s8, int s9, int s10, int s11)
        {
            if (disc == 0)
                return new Ipv4SocketAddress(
                    (ushort)s1,
                    new byte[] {
                        (byte)s2, (byte)s3, (byte)s4, (byte)s5,
                    });
            return new Ipv6SocketAddress(
                (ushort)s1, (uint)s2,
                new ushort[] {
                    (ushort)s3, (ushort)s4, (ushort)s5, (ushort)s6,
                    (ushort)s7, (ushort)s8, (ushort)s9, (ushort)s10,
                },
                (uint)s11);
        }

        // Write a variant ip-socket-address record at <ptr>.
        // Layout: 1B variant disc + 3B pad + 28B max-payload =
        // 32 bytes total, align 4.
        //   ipv4 case: port(u16)@+4, address(4B)@+6 — 6 bytes used.
        //   ipv6 case: port@+4, 2B pad@+6, flow(u32)@+8,
        //     8×u16(16B)@+12, scope(u32)@+28 — 28 bytes used.
        private static void WriteIpSocketAddress(byte[] mem, int ptr,
            IpSocketAddress addr)
        {
            if (addr is Ipv4SocketAddress v4)
            {
                mem[ptr] = 0;
                mem[ptr + 1] = 0;
                mem[ptr + 2] = 0;
                mem[ptr + 3] = 0;
                MemoryWriter.WriteU16LE(mem, ptr + 4, v4.Port);
                mem[ptr + 6] = v4.Address[0];
                mem[ptr + 7] = v4.Address[1];
                mem[ptr + 8] = v4.Address[2];
                mem[ptr + 9] = v4.Address[3];
                return;
            }
            var v6 = (Ipv6SocketAddress)addr;
            mem[ptr] = 1;
            mem[ptr + 1] = 0;
            mem[ptr + 2] = 0;
            mem[ptr + 3] = 0;
            MemoryWriter.WriteU16LE(mem, ptr + 4, v6.Port);
            mem[ptr + 6] = 0;
            mem[ptr + 7] = 0;
            MemoryWriter.WriteU32LE(mem, ptr + 8, v6.FlowInfo);
            for (int i = 0; i < 8; i++)
                MemoryWriter.WriteU16LE(mem, ptr + 12 + i * 2,
                    v6.Address[i]);
            MemoryWriter.WriteU32LE(mem, ptr + 28, v6.ScopeId);
        }
    }
}
