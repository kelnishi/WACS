// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>WIT record
    /// <c>wasi:sockets/udp.incoming-datagram</c>:
    /// <code>record incoming-datagram {
    ///     data: list&lt;u8&gt;,
    ///     remote-address: ip-socket-address,
    /// }</code>
    /// Wire size 40, align 4: 8 bytes for the list (ptr, len)
    /// + 32 bytes for the variant.</summary>
    public sealed class IncomingDatagram
    {
        public byte[] Data { get; }
        public IpSocketAddress RemoteAddress { get; }

        public IncomingDatagram(byte[] data, IpSocketAddress remoteAddress)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            RemoteAddress = remoteAddress
                ?? throw new ArgumentNullException(nameof(remoteAddress));
        }
    }

    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/udp@0.2.x</c>'s
    /// <c>incoming-datagram-stream</c> resource. Pull-stream
    /// of UDP datagrams the socket has received from the
    /// network. Pairs with <see cref="OutgoingDatagramStream"/>
    /// — the two are produced together by
    /// <c>udp-socket.%stream</c>.
    /// </summary>
    [WasiResource("incoming-datagram-stream")]
    public class IncomingDatagramStream : IDisposable
    {
        /// <summary>Pull up to <paramref name="maxResults"/>
        /// datagrams from the socket's receive queue.
        /// Returns an empty array when the queue is empty.
        /// Default returns empty; concrete impls override
        /// with real socket reads.</summary>
        [WasiErrorResult]
        public virtual IncomingDatagram[] Receive(ulong maxResults)
            => System.Array.Empty<IncomingDatagram>();

        /// <summary>Pollable that fires when at least one
        /// datagram is available to receive.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/udp@0.2.x</c>'s
    /// <c>outgoing-datagram-stream</c> resource. Push-stream
    /// of UDP datagrams the socket sends to the network.
    /// </summary>
    [WasiResource("outgoing-datagram-stream")]
    public class OutgoingDatagramStream : IDisposable
    {
        /// <summary>Maximum number of datagrams that can be
        /// enqueued for transmission right now without
        /// blocking. Default returns a generous fixed value;
        /// concrete impls override based on real socket
        /// buffer state.</summary>
        [WasiErrorResult]
        [WasiMethodName("check-send")]
        public virtual ulong CheckSend() => 64;

        /// <summary>Pollable that fires when the next
        /// <c>send</c> won't block.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }
}
