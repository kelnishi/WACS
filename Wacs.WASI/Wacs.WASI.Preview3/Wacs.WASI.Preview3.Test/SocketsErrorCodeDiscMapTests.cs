// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview3.Sockets;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice KK coverage: wasi:sockets/types.error-code
    /// (15 arms) and wasi:sockets/ip-name-lookup.error-code
    /// (6 arms) are two distinct WIT variants but share the
    /// host's C# Sockets.ErrorCode enum. Pin the per-variant
    /// discriminant mappings.
    /// </summary>
    public class SocketsErrorCodeDiscMapTests
    {
        // ---- sockets/types disc map (15 arms) ---------------------

        [Theory]
        [InlineData(ErrorCode.AccessDenied,        0)]
        [InlineData(ErrorCode.NotSupported,        1)]
        [InlineData(ErrorCode.InvalidArgument,     2)]
        [InlineData(ErrorCode.OutOfMemory,         3)]
        [InlineData(ErrorCode.Timeout,             4)]
        [InlineData(ErrorCode.InvalidState,        5)]
        [InlineData(ErrorCode.AddressNotBindable,  6)]
        [InlineData(ErrorCode.AddressInUse,        7)]
        [InlineData(ErrorCode.RemoteUnreachable,   8)]
        [InlineData(ErrorCode.ConnectionRefused,   9)]
        [InlineData(ErrorCode.ConnectionBroken,    10)]
        [InlineData(ErrorCode.ConnectionReset,     11)]
        [InlineData(ErrorCode.ConnectionAborted,   12)]
        [InlineData(ErrorCode.DatagramTooLarge,    13)]
        [InlineData(ErrorCode.Other,               14)]
        public void SocketsTypes_disc_map(ErrorCode code, int expected)
        {
            Assert.Equal((byte)expected,
                WasiPreview3Host.MapSocketsTypesErrorCodeDisc(code));
        }

        [Theory]
        [InlineData(ErrorCode.NameUnresolvable)]
        [InlineData(ErrorCode.TemporaryResolverFailure)]
        [InlineData(ErrorCode.PermanentResolverFailure)]
        public void SocketsTypes_disc_map_rejects_ip_name_lookup_arms(
            ErrorCode code)
        {
            Assert.Throws<ArgumentException>(
                () => WasiPreview3Host.MapSocketsTypesErrorCodeDisc(code));
        }

        // ---- ip-name-lookup disc map (6 arms) ---------------------

        [Theory]
        [InlineData(ErrorCode.AccessDenied,             0)]
        [InlineData(ErrorCode.InvalidArgument,          1)]
        [InlineData(ErrorCode.NameUnresolvable,         2)]
        [InlineData(ErrorCode.TemporaryResolverFailure, 3)]
        [InlineData(ErrorCode.PermanentResolverFailure, 4)]
        [InlineData(ErrorCode.Other,                    5)]
        public void IpNameLookup_disc_map(ErrorCode code, int expected)
        {
            Assert.Equal((byte)expected,
                WasiPreview3Host.MapIpNameLookupErrorCodeDisc(code));
        }

        [Theory]
        [InlineData(ErrorCode.NotSupported)]
        [InlineData(ErrorCode.Timeout)]
        [InlineData(ErrorCode.AddressInUse)]
        [InlineData(ErrorCode.ConnectionRefused)]
        public void IpNameLookup_disc_map_rejects_sockets_types_arms(
            ErrorCode code)
        {
            Assert.Throws<ArgumentException>(
                () => WasiPreview3Host.MapIpNameLookupErrorCodeDisc(code));
        }
    }
}
