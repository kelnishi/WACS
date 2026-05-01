// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASI.Preview1.Types;
using fd = System.UInt32;
using ptr = System.UInt32;
using size = System.UInt32;

namespace Wacs.WASI.Preview1
{
    public class Sock : IBindable
    {
        private readonly State _state;

        public Sock(State state) => _state = state;

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext, fd, FdFlags, ptr, ErrNo>>((module, "sock_accept"), SockAccept);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, RiFlags, ptr, ptr, ErrNo>>((module, "sock_recv"), SockRecv);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, SiFlags, ptr, ErrNo>>((module, "sock_send"), SockSend);
            runtime.BindHostFunction<Func<ExecContext, fd, SdFlags, ErrNo>>((module, "sock_shutdown"), SockShutdown);
        }

        public ErrNo SockAccept(ExecContext ctx, fd sock, FdFlags flags, ptr ro_fd)
        {
            if (!_state.FileDescriptors.TryGetValue(sock, out var fileDescriptor))
                return ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream)
                return ErrNo.NotSock;
            if (fileDescriptor.Socket is null || !fileDescriptor.IsListening)
                return ErrNo.Inval;

            try
            {
                Socket conn;
                if ((fileDescriptor.Flags & FdFlags.NonBlock) != 0)
                {
                    // Non-blocking listener: only accept if a connection
                    // is already pending; otherwise return EAGAIN.
                    if (!fileDescriptor.Socket.Poll(0, SelectMode.SelectRead))
                        return ErrNo.Again;
                    conn = fileDescriptor.Socket.Accept();
                }
                else
                {
                    // Task.Run escapes any captured sync context (avoids
                    // deadlock under UI hosts like Unity).
                    conn = Task.Run(() => fileDescriptor.Socket.AcceptAsync())
                        .GetAwaiter().GetResult();
                }

                fd newFd = _state.GetNextFd;
                Rights rights = Rights.FD_READ | Rights.FD_WRITE
                    | Rights.POLL_FD_READWRITE | Rights.SOCK_SHUTDOWN
                    | Rights.FD_FDSTAT_SET_FLAGS;
                _state.FileDescriptors[newFd] = new FileDescriptor
                {
                    Fd = newFd,
                    Stream = new SocketStream(conn, isListening: false),
                    Path = "/dev/socket",
                    Access = FileAccess.ReadWrite,
                    IsPreopened = false,
                    Type = Filetype.SocketStream,
                    Rights = rights,
                    InheritedRights = rights,
                    Flags = flags,
                    Socket = conn,
                    IsListening = false,
                };

                ctx.DefaultMemory.WriteInt32((int)ro_fd, (int)newFd);
                return ErrNo.Success;
            }
            catch (SocketException) { return ErrNo.IO; }
            catch (ObjectDisposedException) { return ErrNo.Badf; }
        }

        public ErrNo SockRecv(ExecContext ctx, fd sock,
            ptr ri_data, size ri_datalen, RiFlags ri_flags,
            ptr ro_data_len, ptr ro_flags)
        {
            if (!_state.FileDescriptors.TryGetValue(sock, out var fileDescriptor))
                return ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream &&
                fileDescriptor.Type != Filetype.SocketDgram)
                return ErrNo.NotSock;
            if (fileDescriptor.Socket is null || fileDescriptor.IsListening)
                return ErrNo.Inval;

            var mem = ctx.DefaultMemory;
            var iovs = mem.ReadStructs<IoVec>(ri_data, ri_datalen);
            var sockFlags = SocketFlags.None;
            if ((ri_flags & RiFlags.RecvPeek) != 0)    sockFlags |= SocketFlags.Peek;
            // RiFlags.RecvWaitAll → loop until full or EOF below.

            int totalRead = 0;
            try
            {
                foreach (var iov in iovs)
                {
                    if (iov.bufLen == 0) continue;
                    var dest = new byte[iov.bufLen];
                    if ((ri_flags & RiFlags.RecvWaitAll) != 0)
                    {
                        int got = 0;
                        while (got < dest.Length)
                        {
                            int n = fileDescriptor.Socket.Receive(
                                dest, got, dest.Length - got, sockFlags);
                            if (n <= 0) break;
                            got += n;
                        }
                        new ReadOnlySpan<byte>(dest, 0, got).CopyTo(
                            mem[(int)iov.bufPtr..(int)(iov.bufPtr + (uint)got)]);
                        totalRead += got;
                        if (got < iov.bufLen) break;
                    }
                    else
                    {
                        int n = fileDescriptor.Socket.Receive(dest, 0, dest.Length, sockFlags);
                        new ReadOnlySpan<byte>(dest, 0, n).CopyTo(
                            mem[(int)iov.bufPtr..(int)(iov.bufPtr + (uint)n)]);
                        totalRead += n;
                        if (n < iov.bufLen) break;
                    }
                }
                mem.WriteInt32((int)ro_data_len, totalRead);
                // No out-of-band bits implemented; flags stay 0.
                mem.WriteInt32((int)ro_flags, 0);
                return ErrNo.Success;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                { return ErrNo.Again; }
            catch (SocketException) { return ErrNo.IO; }
            catch (ObjectDisposedException) { return ErrNo.Badf; }
        }

        public ErrNo SockSend(ExecContext ctx, fd sock,
            ptr si_data, size si_data_len, SiFlags si_flags,
            ptr ret_data_len)
        {
            if (!_state.FileDescriptors.TryGetValue(sock, out var fileDescriptor))
                return ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream &&
                fileDescriptor.Type != Filetype.SocketDgram)
                return ErrNo.NotSock;
            if (fileDescriptor.Socket is null || fileDescriptor.IsListening)
                return ErrNo.Inval;

            // si_flags is reserved in WASI Preview 1 (no defined bits);
            // accept any value.
            var mem = ctx.DefaultMemory;
            var iovs = mem.ReadStructs<IoVec>(si_data, si_data_len);
            int totalSent = 0;
            try
            {
                foreach (var iov in iovs)
                {
                    if (iov.bufLen == 0) continue;
                    var src = mem[(int)iov.bufPtr..(int)(iov.bufPtr + iov.bufLen)];
                    var buf = src.ToArray();
                    int n = fileDescriptor.Socket.Send(buf, 0, buf.Length, SocketFlags.None);
                    totalSent += n;
                    if (n < iov.bufLen) break;
                }
                mem.WriteInt32((int)ret_data_len, totalSent);
                return ErrNo.Success;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                { return ErrNo.Again; }
            catch (SocketException) { return ErrNo.IO; }
            catch (ObjectDisposedException) { return ErrNo.Badf; }
        }

        public ErrNo SockShutdown(ExecContext ctx, fd sock, SdFlags how)
        {
            if (!_state.FileDescriptors.TryGetValue(sock, out var fileDescriptor))
                return ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream &&
                fileDescriptor.Type != Filetype.SocketDgram)
                return ErrNo.NotSock;
            if (fileDescriptor.Socket is null)
                return ErrNo.NotConn;

            SocketShutdown dir;
            if ((how & SdFlags.Rd) != 0 && (how & SdFlags.Wr) != 0)
                dir = SocketShutdown.Both;
            else if ((how & SdFlags.Rd) != 0)
                dir = SocketShutdown.Receive;
            else if ((how & SdFlags.Wr) != 0)
                dir = SocketShutdown.Send;
            else
                return ErrNo.Inval;

            try
            {
                fileDescriptor.Socket.Shutdown(dir);
                return ErrNo.Success;
            }
            catch (SocketException) { return ErrNo.IO; }
            catch (ObjectDisposedException) { return ErrNo.Badf; }
        }
    }
}
