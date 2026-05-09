// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Sockets;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/ip-name-lookup@0.2.8</c>'s
    /// <c>resolve-address-stream</c> resource. A resolver
    /// pull-stream — guest calls
    /// <see cref="ResolveNextAddress"/> in a loop, getting
    /// one address at a time until None signals "exhausted".
    ///
    /// <para>The base class is an empty stream that returns
    /// Ok(None) on every call — useful as a stub. The
    /// production-ready subclass <see cref="DnsResolveAddressStream"/>
    /// is array-backed; its constructor takes the resolved
    /// IPs and the stream walks them. Hosts can implement other
    /// strategies by overriding
    /// <see cref="ResolveNextAddress"/> directly.</para>
    /// </summary>
    [WasiResource("resolve-address-stream")]
    public class ResolveAddressStream : IResolveAddressStream, IDisposable
    {
        /// <summary>Pull the next resolved address, or None
        /// when the resolution is exhausted. Default returns
        /// Ok(None) — empty stream.</summary>
        public virtual Result<Option<IpAddress>, ErrorCode> ResolveNextAddress()
            => Result<Option<IpAddress>, ErrorCode>.FromOk(
                Option<IpAddress>.None);

        /// <summary>Pollable that fires when more addresses
        /// are available or the stream is fully resolved.
        /// Default returns an always-ready pollable since the
        /// resolution is already complete by the time the
        /// stream is constructed.</summary>
        public virtual IPollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>Concrete <see cref="ResolveAddressStream"/>
    /// backed by a pre-resolved array of <see cref="IPAddress"/>
    /// values from <see cref="Dns.GetHostAddresses"/>. Walks
    /// them on each <see cref="ResolveNextAddress"/> call;
    /// returns Ok(None) once exhausted.</summary>
    public sealed class DnsResolveAddressStream : ResolveAddressStream
    {
        private readonly IPAddress[] _addresses;
        private int _pos;

        public DnsResolveAddressStream(IPAddress[] addresses)
        {
            _addresses = addresses
                ?? throw new ArgumentNullException(nameof(addresses));
        }

        public override Result<Option<IpAddress>, ErrorCode>
            ResolveNextAddress()
        {
            if (_pos >= _addresses.Length)
                return Result<Option<IpAddress>, ErrorCode>.FromOk(
                    Option<IpAddress>.None);
            var ip = _addresses[_pos++];
            var item = ConvertToIpAddress(ip);
            return Result<Option<IpAddress>, ErrorCode>.FromOk(
                Option<IpAddress>.Some(item));
        }

        private static IpAddress ConvertToIpAddress(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b4 = ip.GetAddressBytes();
                return new IpAddress.IpAddressIpv4(
                    (b4[0], b4[1], b4[2], b4[3]));
            }
            // IPv6: 16 bytes → 8 u16 groups (network order).
            var b = ip.GetAddressBytes();
            return new IpAddress.IpAddressIpv6((
                ToU16(b, 0), ToU16(b, 2),
                ToU16(b, 4), ToU16(b, 6),
                ToU16(b, 8), ToU16(b, 10),
                ToU16(b, 12), ToU16(b, 14)));
        }

        private static ushort ToU16(byte[] b, int offset) =>
            (ushort)((b[offset] << 8) | b[offset + 1]);
    }

    /// <summary>Default <see cref="IIpNameLookup"/> impl backed
    /// by <see cref="Dns.GetHostAddresses"/> (synchronous —
    /// the WIT signature is synchronous; production hosts
    /// concerned about blocking should override with an
    /// async-driven implementation that returns a stream
    /// completing later via the pollable).
    ///
    /// <para>Maps DNS exceptions to the closest WASI
    /// <see cref="ErrorCode"/> case:
    /// <list type="bullet">
    /// <item>NXDOMAIN / unknown name → <c>NameUnresolvable</c></item>
    /// <item>Transient failure → <c>TemporaryResolverFailure</c></item>
    /// <item>Anything else → <c>PermanentResolverFailure</c></item>
    /// </list></para></summary>
    public sealed class IpNameLookup : IIpNameLookup
    {
        public Result<IResolveAddressStream, ErrorCode> ResolveAddresses(
            INetwork network, string name)
        {
            if (string.IsNullOrEmpty(name))
                return Result<IResolveAddressStream, ErrorCode>.FromErr(
                    ErrorCode.InvalidArgument);

            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(name);
            }
            catch (SocketException se)
                when (se.SocketErrorCode == SocketError.HostNotFound)
            {
                return Result<IResolveAddressStream, ErrorCode>.FromErr(
                    ErrorCode.NameUnresolvable);
            }
            catch (SocketException se)
                when (se.SocketErrorCode == SocketError.TryAgain)
            {
                return Result<IResolveAddressStream, ErrorCode>.FromErr(
                    ErrorCode.TemporaryResolverFailure);
            }
            catch (SocketException)
            {
                return Result<IResolveAddressStream, ErrorCode>.FromErr(
                    ErrorCode.PermanentResolverFailure);
            }
            catch (ArgumentException)
            {
                // Bad hostname syntax (e.g. control chars).
                return Result<IResolveAddressStream, ErrorCode>.FromErr(
                    ErrorCode.InvalidArgument);
            }

            return Result<IResolveAddressStream, ErrorCode>.FromOk(
                new DnsResolveAddressStream(addresses));
        }
    }
}
