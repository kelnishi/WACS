// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview3.Sockets
{
    /// <summary>
    /// Host interface for
    /// <c>wasi:sockets/ip-name-lookup@0.3.0-rc-2026-03-15</c>.
    /// One async method: resolve a hostname to a list of IP
    /// addresses.
    /// </summary>
    public interface IIpNameLookup
    {
        /// <summary>
        /// <c>resolve-addresses: async func(name: string)
        /// -&gt; result&lt;list&lt;ip-address&gt;, error-code&gt;</c>.
        /// On the err path throws <see cref="SocketsException"/>
        /// — typically <see cref="ErrorCode.NameUnresolvable"/>
        /// for unknown hosts,
        /// <see cref="ErrorCode.TemporaryResolverFailure"/> for
        /// transient DNS errors.
        /// </summary>
        Task<IReadOnlyList<IpAddress>> ResolveAddressesAsync(
            string name, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default <see cref="IIpNameLookup"/> implementation
    /// backed by <see cref="Dns.GetHostAddressesAsync(string,
    /// CancellationToken)"/>. Returns both IPv4 and IPv6
    /// addresses where available, in the order the OS resolver
    /// reports.
    ///
    /// <para>The spec allows
    /// "deterministic environments" to refuse resolution
    /// (returning <see cref="ErrorCode.PermanentResolverFailure"/>);
    /// the default impl is non-deterministic since it consults
    /// the OS resolver. Embedders that want a deterministic /
    /// sandboxed surface should replace this with a custom
    /// impl that returns a fixed mapping.</para>
    /// </summary>
    public sealed class DnsBackedNameLookup : IIpNameLookup
    {
        public async Task<IReadOnlyList<IpAddress>> ResolveAddressesAsync(
            string name, CancellationToken cancellationToken = default)
        {
            if (name == null)
                throw new SocketsException(
                    ErrorCode.InvalidArgument, "name is null");

            IPAddress[] resolved;
            try
            {
                // The (string, CancellationToken) overload is
                // .NET 6+; netstandard2.1 only has the single-arg
                // form. Compose with WaitAsync for cancellation
                // on net6+, fall back to a synchronous-check
                // path on older runtimes.
#if NET6_0_OR_GREATER
                resolved = await Dns.GetHostAddressesAsync(
                    name, cancellationToken).ConfigureAwait(false);
#else
                cancellationToken.ThrowIfCancellationRequested();
                resolved = await Dns.GetHostAddressesAsync(name)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
#endif
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException sx) when (
                sx.SocketErrorCode == SocketError.HostNotFound)
            {
                throw new SocketsException(
                    ErrorCode.NameUnresolvable,
                    $"host '{name}' not found");
            }
            catch (SocketException sx) when (
                sx.SocketErrorCode == SocketError.TryAgain)
            {
                throw new SocketsException(
                    ErrorCode.TemporaryResolverFailure, sx.Message);
            }
            catch (SocketException sx)
            {
                throw new SocketsException(
                    ErrorCode.PermanentResolverFailure, sx.Message);
            }
            catch (ArgumentException ax)
            {
                throw new SocketsException(
                    ErrorCode.InvalidArgument, ax.Message);
            }
            catch (Exception ex)
            {
                throw new SocketsException(
                    ErrorCode.Other, ex.Message);
            }

            var list = new List<IpAddress>(resolved.Length);
            foreach (var addr in resolved)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    var bytes = addr.GetAddressBytes();
                    list.Add(IpAddress.Ipv4(new Ipv4Address(
                        bytes[0], bytes[1], bytes[2], bytes[3])));
                }
                else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    var bytes = addr.GetAddressBytes(); // 16 bytes BE
                    list.Add(IpAddress.Ipv6(new Ipv6Address(
                        (ushort)((bytes[0] << 8) | bytes[1]),
                        (ushort)((bytes[2] << 8) | bytes[3]),
                        (ushort)((bytes[4] << 8) | bytes[5]),
                        (ushort)((bytes[6] << 8) | bytes[7]),
                        (ushort)((bytes[8] << 8) | bytes[9]),
                        (ushort)((bytes[10] << 8) | bytes[11]),
                        (ushort)((bytes[12] << 8) | bytes[13]),
                        (ushort)((bytes[14] << 8) | bytes[15]))));
                }
                // Other address families (Unix sockets etc.) are
                // not representable as ip-address; skip silently.
            }
            return list;
        }
    }

    /// <summary>
    /// Stub <see cref="IIpNameLookup"/> that refuses all
    /// resolution attempts with
    /// <see cref="ErrorCode.PermanentResolverFailure"/>. Used
    /// as a default in DI scenarios where the embedder hasn't
    /// configured a real resolver — fails the resolution cleanly
    /// instead of silently leaking the host's DNS configuration.
    /// </summary>
    public sealed class NoNameLookup : IIpNameLookup
    {
        public Task<IReadOnlyList<IpAddress>> ResolveAddressesAsync(
            string name, CancellationToken cancellationToken = default)
            => Task.FromException<IReadOnlyList<IpAddress>>(
                new SocketsException(
                    ErrorCode.PermanentResolverFailure,
                    "no name lookup provider configured " +
                    "on this WASI Preview 3 host."));
    }
}
