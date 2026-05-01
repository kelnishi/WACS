// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

using System;
using System.IO;
using System.Net.Sockets;

namespace Wacs.WASI.Preview1
{
    // Stream wrapper around a connected or listening Socket. Connected
    // sockets implement Read/Write/CanRead/CanWrite so the existing
    // FdRead/FdWrite iovec paths work unchanged. Listening sockets
    // throw NotSupported on Read/Write — Sock.SockAccept is the only
    // operation that mints a connected SocketStream from one.
    //
    // We don't reuse System.Net.NetworkStream because it doesn't surface
    // Socket.Poll cleanly and it Dispose's the underlying socket on
    // close — we want to keep Socket lifetime tied to the FileDescriptor
    // not the wrapping stream.
    public sealed class SocketStream : Stream
    {
        public Socket Socket { get; }
        public bool IsListening { get; }

        public SocketStream(Socket socket, bool isListening)
        {
            Socket = socket;
            IsListening = isListening;
        }

        public override bool CanRead => !IsListening;
        public override bool CanWrite => !IsListening;
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (IsListening) throw new NotSupportedException();
            return Socket.Receive(buffer, offset, count, SocketFlags.None);
        }

#if NET8_0_OR_GREATER
        public override int Read(Span<byte> buffer)
        {
            if (IsListening) throw new NotSupportedException();
            return Socket.Receive(buffer, SocketFlags.None);
        }
#endif

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (IsListening) throw new NotSupportedException();
            Socket.Send(buffer, offset, count, SocketFlags.None);
        }

        public override void Flush() { /* no-op for sockets */ }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Socket.Dispose();
            base.Dispose(disposing);
        }
    }
}
