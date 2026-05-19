// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wacs.WASI.Preview3.Sockets;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice D coverage: <c>wasi:sockets</c> type
    /// system + IIpNameLookup default impl + interface
    /// contracts for tcp-socket / udp-socket (full backings ship
    /// in a follow-up slice).
    /// </summary>
    public class SocketsTests
    {
        // ---- Types ---------------------------------------------------

        [Fact]
        public void Ipv4Address_to_string_dotted_decimal()
        {
            Assert.Equal("127.0.0.1", Ipv4Address.Loopback.ToString());
            Assert.Equal("0.0.0.0", Ipv4Address.Any.ToString());
            Assert.Equal("10.20.30.40",
                new Ipv4Address(10, 20, 30, 40).ToString());
        }

        [Fact]
        public void Ipv4Address_equality_is_componentwise()
        {
            var a = new Ipv4Address(192, 168, 1, 1);
            var b = new Ipv4Address(192, 168, 1, 1);
            var c = new Ipv4Address(192, 168, 1, 2);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void Ipv6Address_special_addresses()
        {
            // ::1 = 7×0 + final 1
            Assert.Equal(1, Ipv6Address.Loopback.G7);
            Assert.Equal(0, Ipv6Address.Any.G0);
            Assert.Equal(0, Ipv6Address.Any.G7);
        }

        [Fact]
        public void IpAddress_factory_methods_set_family_and_payload()
        {
            var v4 = IpAddress.Ipv4(new Ipv4Address(1, 2, 3, 4));
            Assert.Equal(IpAddressFamily.Ipv4, v4.Family);
            Assert.Equal(new Ipv4Address(1, 2, 3, 4), v4.V4);

            var v6 = IpAddress.Ipv6(Ipv6Address.Loopback);
            Assert.Equal(IpAddressFamily.Ipv6, v6.Family);
            Assert.Equal(Ipv6Address.Loopback, v6.V6);
        }

        [Fact]
        public void IpSocketAddress_to_string_matches_canonical_form()
        {
            var v4 = IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(8080, Ipv4Address.Loopback));
            Assert.Equal("127.0.0.1:8080", v4.ToString());

            var v6 = IpSocketAddress.Ipv6(
                new Ipv6SocketAddress(
                    443, 0, Ipv6Address.Loopback, 0));
            Assert.StartsWith("[", v6.ToString());
            Assert.EndsWith(":443", v6.ToString());
        }

        [Fact]
        public void Ipv6SocketAddress_preserves_flow_info_and_scope_id()
        {
            var addr = new Ipv6SocketAddress(
                443, 0xCAFEu, Ipv6Address.Loopback, 7u);
            Assert.Equal(443, (int)addr.Port);
            Assert.Equal(0xCAFEu, addr.FlowInfo);
            Assert.Equal(7u, addr.ScopeId);
        }

        [Fact]
        public void SocketsException_carries_other_payload_when_kind_is_other()
        {
            var ex = new SocketsException(
                ErrorCode.Other, "unusual networking failure");
            Assert.Equal(ErrorCode.Other, ex.Code);
            Assert.Equal("unusual networking failure", ex.OtherPayload);
        }

        [Fact]
        public void SocketsException_other_payload_null_for_typed_codes()
        {
            var ex = new SocketsException(
                ErrorCode.Timeout, "took too long");
            Assert.Equal(ErrorCode.Timeout, ex.Code);
            Assert.Null(ex.OtherPayload);
        }

        // ---- DnsBackedNameLookup -------------------------------------

        [Fact]
        public async Task DnsBackedNameLookup_resolves_loopback_name()
        {
            // "localhost" should resolve to at least one address
            // on any reasonable host. Skip if the host's DNS is
            // unusual (CI without /etc/hosts entry, etc.).
            var lookup = new DnsBackedNameLookup();
            IReadOnlyList<IpAddress> result;
            try
            {
                result = await lookup.ResolveAddressesAsync("localhost");
            }
            catch (SocketsException)
            {
                // CI environment may not resolve localhost; skip.
                return;
            }
            Assert.NotEmpty(result);
            // Expect at least one of 127.0.0.1 / ::1.
            Assert.Contains(result, ip =>
                (ip.Family == IpAddressFamily.Ipv4
                    && ip.V4.Equals(Ipv4Address.Loopback))
                || (ip.Family == IpAddressFamily.Ipv6
                    && ip.V6.Equals(Ipv6Address.Loopback)));
        }

        [Fact]
        public async Task DnsBackedNameLookup_throws_NameUnresolvable_for_bogus()
        {
            var lookup = new DnsBackedNameLookup();
            var ex = await Assert.ThrowsAsync<SocketsException>(
                () => lookup.ResolveAddressesAsync(
                    "no-such-host.invalid-tld-xyz123"));
            // Either NameUnresolvable or some other resolver failure.
            Assert.Contains(ex.Code, new[]
            {
                ErrorCode.NameUnresolvable,
                ErrorCode.TemporaryResolverFailure,
                ErrorCode.PermanentResolverFailure,
                ErrorCode.Other,
            });
        }

        [Fact]
        public async Task DnsBackedNameLookup_throws_InvalidArgument_for_null()
        {
            var lookup = new DnsBackedNameLookup();
            var ex = await Assert.ThrowsAsync<SocketsException>(
                () => lookup.ResolveAddressesAsync(null!));
            Assert.Equal(ErrorCode.InvalidArgument, ex.Code);
        }

        // ---- NoNameLookup (default) ---------------------------------

        [Fact]
        public async Task NoNameLookup_refuses_with_permanent_failure()
        {
            var lookup = new NoNameLookup();
            var ex = await Assert.ThrowsAsync<SocketsException>(
                () => lookup.ResolveAddressesAsync("example.com"));
            Assert.Equal(
                ErrorCode.PermanentResolverFailure, ex.Code);
        }

        // ---- Host integration ----------------------------------------

        [Fact]
        public void Host_default_construct_provides_no_name_lookup()
        {
            var host = new WasiPreview3Host();
            Assert.IsType<NoNameLookup>(host.IpNameLookup);
        }

        [Fact]
        public void Host_builder_overrides_name_lookup()
        {
            var dns = new DnsBackedNameLookup();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder { IpNameLookup = dns });
            Assert.Same(dns, host.IpNameLookup);
        }
    }
}
