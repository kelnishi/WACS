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
    ///
    /// <para>Every host method that the WIT spec marks
    /// <c>result&lt;X, error-code&gt;</c> returns
    /// <see cref="Result{TOk,TErr}"/> over
    /// <see cref="ErrorCode"/>; the bindings encode both
    /// branches faithfully (Ok writes the payload, Err writes
    /// outer disc=1 + the ErrorCode value at offset+1, with
    /// the rest of the Ok-payload area zeroed). error-code is
    /// a 21-case u8 enum (no payload), so result.align always
    /// matches the Ok payload's alignment and the Err-side
    /// shares the retArea size with the Ok-side.</para>
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
        // error-code is a 21-case u8 enum (no payload), so it
        // contributes align=1 to every result variant. The
        // result.align therefore matches the Ok-side payload's
        // alignment; Ok-payload offset = max_align. The Err
        // side writes outer disc=1 at retArea+0, the
        // ErrorCode byte at retArea+1, and zeroes the rest of
        // the payload area.

        // Encode the Err side: outer disc=1 at retArea+0,
        // ErrorCode byte at retArea+1, zero through retArea+totalSize.
        private static void WriteErrCode(byte[] mem, int retArea,
            int totalSize, ErrorCode code)
        {
            mem[retArea] = 1;
            mem[retArea + 1] = (byte)code;
            for (int i = 2; i < totalSize; i++) mem[retArea + i] = 0;
        }

        // result<_, error-code>: 2 bytes (1 disc + 1 byte).
        private static void WriteResultUnit(byte[] mem, int retArea,
            Result<Unit, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                return;
            }
            WriteErrCode(mem, retArea, 2, r.Err);
        }

        // result<bool, error-code>: 2 bytes (disc + bool).
        private static void WriteResultBool(byte[] mem, int retArea,
            Result<bool, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = r.Ok ? (byte)1 : (byte)0;
                return;
            }
            WriteErrCode(mem, retArea, 2, r.Err);
        }

        // result<u8, error-code>: 2 bytes (disc + u8).
        private static void WriteResultU8(byte[] mem, int retArea,
            Result<byte, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = r.Ok;
                return;
            }
            WriteErrCode(mem, retArea, 2, r.Err);
        }

        // result<u32, error-code>: 8 bytes (disc + 3 pad + u32).
        private static void WriteResultU32(byte[] mem, int retArea,
            Result<uint, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                MemoryWriter.WriteU32LE(mem, retArea + 4, r.Ok);
                return;
            }
            WriteErrCode(mem, retArea, 8, r.Err);
        }

        // result<u64, error-code>: 16 bytes (disc + 7 pad + u64).
        private static void WriteResultU64(byte[] mem, int retArea,
            Result<ulong, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
                MemoryWriter.WriteU64LE(mem, retArea + 8, r.Ok);
                return;
            }
            WriteErrCode(mem, retArea, 16, r.Err);
        }

        // result<own<X>, error-code>: 8 bytes (disc + 3 pad +
        // handle). Caller supplies a Result<int, ErrorCode>
        // whose Ok-side handle has already been allocated via
        // the appropriate resource table; the encoder just
        // writes the handle bits.
        private static void WriteResultHandle(byte[] mem, int retArea,
            Result<int, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                MemoryWriter.WriteI32LE(mem, retArea + 4, r.Ok);
                return;
            }
            WriteErrCode(mem, retArea, 8, r.Err);
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
                return new IpSocketAddress.IpSocketAddressIpv4(
                    new Ipv4SocketAddress {
                        Port = (ushort)s1,
                        Address = (
                            (byte)s2, (byte)s3,
                            (byte)s4, (byte)s5),
                    });
            return new IpSocketAddress.IpSocketAddressIpv6(
                new Ipv6SocketAddress {
                    Port = (ushort)s1,
                    FlowInfo = (uint)s2,
                    Address = (
                        (ushort)s3, (ushort)s4,
                        (ushort)s5, (ushort)s6,
                        (ushort)s7, (ushort)s8,
                        (ushort)s9, (ushort)s10),
                    ScopeId = (uint)s11,
                });
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
            if (addr is IpSocketAddress.IpSocketAddressIpv4 v4Case)
            {
                var v4 = v4Case.Value;
                mem[ptr] = 0;
                mem[ptr + 1] = 0;
                mem[ptr + 2] = 0;
                mem[ptr + 3] = 0;
                MemoryWriter.WriteU16LE(mem, ptr + 4, v4.Port);
                mem[ptr + 6] = v4.Address.Item1;
                mem[ptr + 7] = v4.Address.Item2;
                mem[ptr + 8] = v4.Address.Item3;
                mem[ptr + 9] = v4.Address.Item4;
                return;
            }
            var v6Case = (IpSocketAddress.IpSocketAddressIpv6)addr;
            var v6 = v6Case.Value;
            mem[ptr] = 1;
            mem[ptr + 1] = 0;
            mem[ptr + 2] = 0;
            mem[ptr + 3] = 0;
            MemoryWriter.WriteU16LE(mem, ptr + 4, v6.Port);
            mem[ptr + 6] = 0;
            mem[ptr + 7] = 0;
            MemoryWriter.WriteU32LE(mem, ptr + 8, v6.FlowInfo);
            MemoryWriter.WriteU16LE(mem, ptr + 12, v6.Address.Item1);
            MemoryWriter.WriteU16LE(mem, ptr + 14, v6.Address.Item2);
            MemoryWriter.WriteU16LE(mem, ptr + 16, v6.Address.Item3);
            MemoryWriter.WriteU16LE(mem, ptr + 18, v6.Address.Item4);
            MemoryWriter.WriteU16LE(mem, ptr + 20, v6.Address.Item5);
            MemoryWriter.WriteU16LE(mem, ptr + 22, v6.Address.Item6);
            MemoryWriter.WriteU16LE(mem, ptr + 24, v6.Address.Item7);
            MemoryWriter.WriteU16LE(mem, ptr + 26, v6.Address.Item8);
            MemoryWriter.WriteU32LE(mem, ptr + 28, v6.ScopeId);
        }

        // result<ip-socket-address, error-code>: 36 bytes.
        // outer disc=0 + 3 pad + ip-socket-address (32B) at +4.
        private static void WriteResultIpSocketAddress(byte[] mem, int retArea,
            Result<IpSocketAddress, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                WriteIpSocketAddress(mem, retArea + 4, r.Ok);
                return;
            }
            WriteErrCode(mem, retArea, 36, r.Err);
        }
    }
}
