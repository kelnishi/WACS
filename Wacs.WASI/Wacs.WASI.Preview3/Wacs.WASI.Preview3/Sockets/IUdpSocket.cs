// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview3.Sockets
{
    /// <summary>
    /// Host interface for the WIT
    /// <c>resource udp-socket</c> in
    /// <c>wasi:sockets/types@0.3.0-rc-2026-03-15</c>.
    /// Connection-less datagram socket — supports both
    /// "unconnected" (send-to/receive-from arbitrary peers) and
    /// "connected" (peer fixed at connect time) modes.
    ///
    /// <para><b>Error reporting.</b> Same shape as
    /// <see cref="ITcpSocket"/>: failures throw
    /// <see cref="SocketsException"/> with a typed error code;
    /// the canon-async binding lowers.</para>
    /// </summary>
    public interface IUdpSocket
    {
        /// <summary><c>bind: func(local-address: ip-socket-address)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        void Bind(IpSocketAddress localAddress);

        /// <summary><c>connect: func(remote-address: ip-socket-address)
        /// -&gt; result&lt;_, error-code&gt;</c>. Sync in the
        /// WIT — UDP connect is just a state change setting the
        /// default peer.</summary>
        void Connect(IpSocketAddress remoteAddress);

        /// <summary><c>disconnect: func() -&gt;
        /// result&lt;_, error-code&gt;</c>.</summary>
        void Disconnect();

        /// <summary><c>send: async func(data: list&lt;u8&gt;,
        /// remote-address: option&lt;ip-socket-address&gt;)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task SendAsync(
            byte[] data, IpSocketAddress? remoteAddress = null,
            CancellationToken cancellationToken = default);

        /// <summary><c>receive: async func() -&gt; result&lt;
        /// tuple&lt;list&lt;u8&gt;, ip-socket-address&gt;,
        /// error-code&gt;</c>. Returns a single received
        /// datagram + its source.</summary>
        Task<(byte[] data, IpSocketAddress remoteAddress)> ReceiveAsync(
            CancellationToken cancellationToken = default);

        IpSocketAddress GetLocalAddress();
        IpSocketAddress GetRemoteAddress();
        IpAddressFamily GetAddressFamily();

        byte GetUnicastHopLimit();
        void SetUnicastHopLimit(byte value);

        ulong GetReceiveBufferSize();
        void SetReceiveBufferSize(ulong value);
        ulong GetSendBufferSize();
        void SetSendBufferSize(ulong value);
    }
}
