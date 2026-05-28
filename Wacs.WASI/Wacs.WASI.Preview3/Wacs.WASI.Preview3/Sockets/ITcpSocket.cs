// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;

namespace Wacs.WASI.Preview3.Sockets
{
    /// <summary>
    /// Host interface for the WIT
    /// <c>resource tcp-socket</c> in
    /// <c>wasi:sockets/types@0.3.0-rc-2026-03-15</c>. Models a
    /// single TCP socket through its full state machine:
    /// <c>unbound → bound → listening</c> for servers, and
    /// <c>unbound → bound → connecting → connected</c> for
    /// clients, with <c>closed</c> as the terminal state.
    ///
    /// <para><b>Error reporting.</b> Methods returning
    /// <c>result&lt;X, error-code&gt;</c> in the WIT throw
    /// <see cref="SocketsException"/> on the err path; the
    /// canon-async binding catches and lowers to the wire
    /// error-code value.</para>
    ///
    /// <para><b>Backing impl.</b> No default System.Net.Sockets-
    /// backed implementation ships in this slice — the state
    /// machine + send/receive stream wiring lands in a follow-up
    /// alongside the BindToRuntime registrations. Embedders that
    /// need TCP sockets today provide their own
    /// <see cref="ITcpSocket"/> implementation.</para>
    /// </summary>
    public interface ITcpSocket
    {
        /// <summary><c>bind: func(local-address: ip-socket-address)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        void Bind(IpSocketAddress localAddress);

        /// <summary><c>connect: async func(remote-address:
        /// ip-socket-address) -&gt; result&lt;_, error-code&gt;</c>.</summary>
        Task ConnectAsync(
            IpSocketAddress remoteAddress,
            CancellationToken cancellationToken = default);

        /// <summary><c>listen: func() -&gt;
        /// result&lt;stream&lt;tcp-socket&gt;, error-code&gt;</c>.
        /// Returns a stream-handle of accepted client sockets;
        /// the canon-binding allocates each client as a
        /// resource handle in the host's tcp-socket table.</summary>
        int Listen(AsyncDispatcher dispatcher);

        /// <summary><c>send: func(data: stream&lt;u8&gt;) -&gt;
        /// future&lt;result&lt;_, error-code&gt;&gt;</c>.</summary>
        (int futureHandle, Task SendCompletion) Send(
            AsyncDispatcher dispatcher, int streamHandle);

        /// <summary><c>receive: func() -&gt;
        /// tuple&lt;stream&lt;u8&gt;, future&lt;result&lt;_,
        /// error-code&gt;&gt;&gt;</c>.</summary>
        (int streamHandle, int futureHandle, Task ReceiveCompletion)
            Receive(AsyncDispatcher dispatcher);

        /// <summary><c>get-local-address: func() -&gt;
        /// result&lt;ip-socket-address, error-code&gt;</c>.</summary>
        IpSocketAddress GetLocalAddress();

        /// <summary><c>get-remote-address: func() -&gt;
        /// result&lt;ip-socket-address, error-code&gt;</c>.</summary>
        IpSocketAddress GetRemoteAddress();

        /// <summary><c>get-is-listening: func() -&gt; bool</c>.</summary>
        bool GetIsListening();

        /// <summary><c>get-address-family: func() -&gt;
        /// ip-address-family</c>.</summary>
        IpAddressFamily GetAddressFamily();

        // ---- Listener config -----------------------------------

        /// <summary><c>set-listen-backlog-size: func(value: u64)
        /// -&gt; result&lt;_, error-code&gt;</c>.</summary>
        void SetListenBacklogSize(ulong value);

        // ---- Keep-alive --------------------------------------

        bool GetKeepAliveEnabled();
        void SetKeepAliveEnabled(bool value);
        /// <summary>Duration in nanoseconds (WIT
        /// <c>duration</c> alias for u64 ns).</summary>
        ulong GetKeepAliveIdleTime();
        void SetKeepAliveIdleTime(ulong nanoseconds);
        ulong GetKeepAliveInterval();
        void SetKeepAliveInterval(ulong nanoseconds);
        uint GetKeepAliveCount();
        void SetKeepAliveCount(uint count);

        // ---- IP options --------------------------------------

        byte GetHopLimit();
        void SetHopLimit(byte value);

        ulong GetReceiveBufferSize();
        void SetReceiveBufferSize(ulong value);
        ulong GetSendBufferSize();
        void SetSendBufferSize(ulong value);
    }
}
