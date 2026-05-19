// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview3.Sockets
{
    /// <summary>
    /// WIT <c>enum ip-address-family</c>.
    /// </summary>
    public enum IpAddressFamily
    {
        Ipv4,
        Ipv6,
    }

    /// <summary>
    /// WIT <c>type ipv4-address = tuple&lt;u8, u8, u8, u8&gt;</c>.
    /// Stored as four bytes in network order.
    /// </summary>
    public readonly struct Ipv4Address : IEquatable<Ipv4Address>
    {
        public byte A { get; }
        public byte B { get; }
        public byte C { get; }
        public byte D { get; }

        public Ipv4Address(byte a, byte b, byte c, byte d)
        {
            A = a; B = b; C = c; D = d;
        }

        /// <summary>The canonical "unspecified" address
        /// <c>0.0.0.0</c> — equivalent to <c>INADDR_ANY</c>.</summary>
        public static Ipv4Address Any => new Ipv4Address(0, 0, 0, 0);

        /// <summary>The loopback address <c>127.0.0.1</c>.</summary>
        public static Ipv4Address Loopback =>
            new Ipv4Address(127, 0, 0, 1);

        public bool Equals(Ipv4Address other) =>
            A == other.A && B == other.B && C == other.C && D == other.D;
        public override bool Equals(object? obj) =>
            obj is Ipv4Address ip && Equals(ip);
        public override int GetHashCode() =>
            (A << 24) | (B << 16) | (C << 8) | D;
        public override string ToString() => $"{A}.{B}.{C}.{D}";
    }

    /// <summary>
    /// WIT <c>type ipv6-address = tuple&lt;u16 x 8&gt;</c>.
    /// Stored as eight ushort groups in network order.
    /// </summary>
    public readonly struct Ipv6Address : IEquatable<Ipv6Address>
    {
        public ushort G0 { get; }
        public ushort G1 { get; }
        public ushort G2 { get; }
        public ushort G3 { get; }
        public ushort G4 { get; }
        public ushort G5 { get; }
        public ushort G6 { get; }
        public ushort G7 { get; }

        public Ipv6Address(
            ushort g0, ushort g1, ushort g2, ushort g3,
            ushort g4, ushort g5, ushort g6, ushort g7)
        {
            G0 = g0; G1 = g1; G2 = g2; G3 = g3;
            G4 = g4; G5 = g5; G6 = g6; G7 = g7;
        }

        /// <summary>The canonical "unspecified" v6 address
        /// <c>::</c> — equivalent to <c>in6addr_any</c>.</summary>
        public static Ipv6Address Any =>
            new Ipv6Address(0, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>The loopback address <c>::1</c>.</summary>
        public static Ipv6Address Loopback =>
            new Ipv6Address(0, 0, 0, 0, 0, 0, 0, 1);

        public bool Equals(Ipv6Address other) =>
            G0 == other.G0 && G1 == other.G1
            && G2 == other.G2 && G3 == other.G3
            && G4 == other.G4 && G5 == other.G5
            && G6 == other.G6 && G7 == other.G7;
        public override bool Equals(object? obj) =>
            obj is Ipv6Address ip && Equals(ip);
        public override int GetHashCode() =>
            unchecked(
                (G0 * 31 + G1) * 31 + G2) * 31 + G3
                ^ ((G4 * 31 + G5) * 31 + G6) * 31 + G7;
        public override string ToString() =>
            $"{G0:x}:{G1:x}:{G2:x}:{G3:x}:{G4:x}:{G5:x}:{G6:x}:{G7:x}";
    }

    /// <summary>
    /// WIT <c>variant ip-address { ipv4(ipv4-address),
    /// ipv6(ipv6-address) }</c>.
    /// </summary>
    public readonly struct IpAddress : IEquatable<IpAddress>
    {
        public IpAddressFamily Family { get; }
        public Ipv4Address V4 { get; }
        public Ipv6Address V6 { get; }

        private IpAddress(
            IpAddressFamily family, Ipv4Address v4, Ipv6Address v6)
        {
            Family = family; V4 = v4; V6 = v6;
        }

        public static IpAddress Ipv4(Ipv4Address addr) =>
            new IpAddress(IpAddressFamily.Ipv4, addr, default);

        public static IpAddress Ipv6(Ipv6Address addr) =>
            new IpAddress(IpAddressFamily.Ipv6, default, addr);

        public bool Equals(IpAddress other)
        {
            if (Family != other.Family) return false;
            return Family == IpAddressFamily.Ipv4
                ? V4.Equals(other.V4) : V6.Equals(other.V6);
        }
        public override bool Equals(object? obj) =>
            obj is IpAddress ip && Equals(ip);
        public override int GetHashCode() =>
            Family == IpAddressFamily.Ipv4
                ? V4.GetHashCode() : V6.GetHashCode();
        public override string ToString() =>
            Family == IpAddressFamily.Ipv4 ? V4.ToString() : V6.ToString();
    }

    /// <summary>
    /// WIT <c>record ipv4-socket-address { port: u16, address: ipv4-address }</c>.
    /// </summary>
    public readonly struct Ipv4SocketAddress : IEquatable<Ipv4SocketAddress>
    {
        public ushort Port { get; }
        public Ipv4Address Address { get; }

        public Ipv4SocketAddress(ushort port, Ipv4Address address)
        {
            Port = port; Address = address;
        }

        public bool Equals(Ipv4SocketAddress other) =>
            Port == other.Port && Address.Equals(other.Address);
        public override bool Equals(object? obj) =>
            obj is Ipv4SocketAddress sa && Equals(sa);
        public override int GetHashCode() =>
            (Port << 16) ^ Address.GetHashCode();
        public override string ToString() => $"{Address}:{Port}";
    }

    /// <summary>
    /// WIT <c>record ipv6-socket-address { port: u16,
    /// flow-info: u32, address: ipv6-address, scope-id: u32 }</c>.
    /// </summary>
    public readonly struct Ipv6SocketAddress : IEquatable<Ipv6SocketAddress>
    {
        public ushort Port { get; }
        public uint FlowInfo { get; }
        public Ipv6Address Address { get; }
        public uint ScopeId { get; }

        public Ipv6SocketAddress(
            ushort port, uint flowInfo, Ipv6Address address, uint scopeId)
        {
            Port = port; FlowInfo = flowInfo;
            Address = address; ScopeId = scopeId;
        }

        public bool Equals(Ipv6SocketAddress other) =>
            Port == other.Port && FlowInfo == other.FlowInfo
            && Address.Equals(other.Address) && ScopeId == other.ScopeId;
        public override bool Equals(object? obj) =>
            obj is Ipv6SocketAddress sa && Equals(sa);
        public override int GetHashCode() =>
            ((Port << 16) ^ (int)FlowInfo)
            ^ (Address.GetHashCode() * 397)
            ^ (int)ScopeId;
        public override string ToString() => $"[{Address}]:{Port}";
    }

    /// <summary>
    /// WIT <c>variant ip-socket-address {
    /// ipv4(ipv4-socket-address), ipv6(ipv6-socket-address) }</c>.
    /// </summary>
    public readonly struct IpSocketAddress : IEquatable<IpSocketAddress>
    {
        public IpAddressFamily Family { get; }
        public Ipv4SocketAddress V4 { get; }
        public Ipv6SocketAddress V6 { get; }

        private IpSocketAddress(
            IpAddressFamily family,
            Ipv4SocketAddress v4, Ipv6SocketAddress v6)
        {
            Family = family; V4 = v4; V6 = v6;
        }

        public static IpSocketAddress Ipv4(Ipv4SocketAddress addr) =>
            new IpSocketAddress(IpAddressFamily.Ipv4, addr, default);

        public static IpSocketAddress Ipv6(Ipv6SocketAddress addr) =>
            new IpSocketAddress(IpAddressFamily.Ipv6, default, addr);

        public bool Equals(IpSocketAddress other)
        {
            if (Family != other.Family) return false;
            return Family == IpAddressFamily.Ipv4
                ? V4.Equals(other.V4) : V6.Equals(other.V6);
        }
        public override bool Equals(object? obj) =>
            obj is IpSocketAddress sa && Equals(sa);
        public override int GetHashCode() =>
            Family == IpAddressFamily.Ipv4
                ? V4.GetHashCode() : V6.GetHashCode();
        public override string ToString() =>
            Family == IpAddressFamily.Ipv4 ? V4.ToString() : V6.ToString();
    }

    /// <summary>
    /// WIT <c>variant error-code</c>. All tag-only cases live
    /// as enum values; the <c>other(option&lt;string&gt;)</c>
    /// payload is carried by
    /// <see cref="SocketsException.OtherPayload"/>.
    ///
    /// <para>Both <c>types.error-code</c> and
    /// <c>ip-name-lookup.error-code</c> are defined in the WIT
    /// — the latter is a strict subset of the former. We model
    /// the union here.</para>
    /// </summary>
    public enum ErrorCode
    {
        AccessDenied,
        NotSupported,
        InvalidArgument,
        OutOfMemory,
        Timeout,
        InvalidState,
        AddressNotBindable,
        AddressInUse,
        RemoteUnreachable,
        ConnectionRefused,
        ConnectionBroken,
        ConnectionReset,
        ConnectionAborted,
        DatagramTooLarge,
        // ip-name-lookup.error-code additionally has:
        NameUnresolvable,
        TemporaryResolverFailure,
        PermanentResolverFailure,
        Other,
    }

    /// <summary>
    /// Host-side exception that carries an
    /// <see cref="ErrorCode"/> for the canon-async binding to
    /// lower into a <c>result&lt;_, error-code&gt;</c> err
    /// case. Method bodies of <see cref="ITcpSocket"/>,
    /// <see cref="IUdpSocket"/>, and <see cref="IIpNameLookup"/>
    /// throw these on failure paths.
    /// </summary>
    public sealed class SocketsException : Exception
    {
        public ErrorCode Code { get; }
        public string? OtherPayload { get; }

        public SocketsException(ErrorCode code, string? message = null)
            : base(message ?? code.ToString())
        {
            Code = code;
            OtherPayload = code == ErrorCode.Other ? message : null;
        }

        public SocketsException(ErrorCode code, Exception inner)
            : base(inner.Message, inner)
        {
            Code = code;
            OtherPayload = code == ErrorCode.Other ? inner.Message : null;
        }
    }
}
