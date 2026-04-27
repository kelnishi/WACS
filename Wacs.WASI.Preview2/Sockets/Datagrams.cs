// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    // The IncomingDatagram and OutgoingDatagram WIT records are
    // emitted by the source generator (settable-prop classes,
    // remote-address as IpSocketAddress / Option<IpSocketAddress>).
    // Hand-written code below adds only the host-side resource
    // impls.

    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/udp@0.2.3</c>'s
    /// <c>incoming-datagram-stream</c> resource. Pull-stream
    /// of UDP datagrams the socket has received from the
    /// network. Pairs with <see cref="OutgoingDatagramStream"/>
    /// — the two are produced together by
    /// <c>udp-socket.%stream</c>.
    /// </summary>
    [WasiResource("incoming-datagram-stream")]
    public class IncomingDatagramStream : IIncomingDatagramStream, IDisposable
    {
        /// <summary>Pull up to <paramref name="maxResults"/>
        /// datagrams from the socket's receive queue.
        /// Returns an empty array when the queue is empty.
        /// Default returns empty Ok; concrete impls override
        /// with real socket reads (and may return
        /// <see cref="ErrorCode"/> failures).</summary>
        public virtual Result<IncomingDatagram[], ErrorCode> Receive(
            ulong maxResults)
            => Result<IncomingDatagram[], ErrorCode>.FromOk(
                System.Array.Empty<IncomingDatagram>());

        /// <summary>Pollable that fires when at least one
        /// datagram is available to receive.</summary>
        public virtual IPollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/udp@0.2.3</c>'s
    /// <c>outgoing-datagram-stream</c> resource. Push-stream
    /// of UDP datagrams the socket sends to the network.
    /// </summary>
    [WasiResource("outgoing-datagram-stream")]
    public class OutgoingDatagramStream : IOutgoingDatagramStream, IDisposable
    {
        /// <summary>Maximum number of datagrams that can be
        /// enqueued for transmission right now without
        /// blocking. Default returns a generous fixed value;
        /// concrete impls override based on real socket
        /// buffer state.</summary>
        public virtual Result<ulong, ErrorCode> CheckSend()
            => Result<ulong, ErrorCode>.FromOk(64UL);

        /// <summary>Send a batch of datagrams. Returns the
        /// number successfully enqueued — short writes are
        /// allowed when the kernel buffer fills mid-batch.
        /// Default returns the input count (assume all sent).
        /// </summary>
        public virtual Result<ulong, ErrorCode> Send(OutgoingDatagram[] datagrams)
            => Result<ulong, ErrorCode>.FromOk((ulong)datagrams.Length);

        /// <summary>Pollable that fires when the next
        /// <c>send</c> won't block.</summary>
        public virtual IPollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }
}
