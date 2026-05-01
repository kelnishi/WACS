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
using Wacs.HostBindings;
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

        // Interpreter wrappers
        public ErrNo SockAccept(ExecContext ctx, fd sock, FdFlags flags, ptr ro_fd)
            => (ErrNo)SockAcceptCore(Clock.WacsHost(ctx), _state, (int)sock, (int)flags, (int)ro_fd);

        public ErrNo SockRecv(ExecContext ctx, fd sock,
            ptr ri_data, size ri_datalen, RiFlags ri_flags,
            ptr ro_data_len, ptr ro_flags)
            => (ErrNo)SockRecvCore(Clock.WacsHost(ctx), _state, (int)sock,
                (int)ri_data, (int)ri_datalen, (int)ri_flags,
                (int)ro_data_len, (int)ro_flags);

        public ErrNo SockSend(ExecContext ctx, fd sock,
            ptr si_data, size si_data_len, SiFlags si_flags,
            ptr ret_data_len)
            => (ErrNo)SockSendCore(Clock.WacsHost(ctx), _state, (int)sock,
                (int)si_data, (int)si_data_len, (int)si_flags,
                (int)ret_data_len);

        public ErrNo SockShutdown(ExecContext ctx, fd sock, SdFlags how)
            => (ErrNo)SockShutdownCore(_state, (int)sock, (int)how);

        // ============================================================
        // AOT-friendly static entry points
        // ============================================================

        [WacsImport("wasi_snapshot_preview1", "sock_accept")]
        public static int SockAcceptCore(WacsHostMemory mem, State state,
            int sock, int flags, int ro_fd)
        {
            if (!state.FileDescriptors.TryGetValue((uint)sock, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream)
                return (int)ErrNo.NotSock;
            if (fileDescriptor.Socket is null || !fileDescriptor.IsListening)
                return (int)ErrNo.Inval;

            try
            {
                Socket conn;
                if ((fileDescriptor.Flags & FdFlags.NonBlock) != 0)
                {
                    if (!fileDescriptor.Socket.Poll(0, SelectMode.SelectRead))
                        return (int)ErrNo.Again;
                    conn = fileDescriptor.Socket.Accept();
                }
                else
                {
                    conn = Task.Run(() => fileDescriptor.Socket.AcceptAsync())
                        .GetAwaiter().GetResult();
                }

                uint newFd = state.GetNextFd;
                Rights rights = Rights.FD_READ | Rights.FD_WRITE
                    | Rights.POLL_FD_READWRITE | Rights.SOCK_SHUTDOWN
                    | Rights.FD_FDSTAT_SET_FLAGS;
                state.FileDescriptors[newFd] = new FileDescriptor
                {
                    Fd = newFd,
                    Stream = new SocketStream(conn, isListening: false),
                    Path = "/dev/socket",
                    Access = FileAccess.ReadWrite,
                    IsPreopened = false,
                    Type = Filetype.SocketStream,
                    Rights = rights,
                    InheritedRights = rights,
                    Flags = (FdFlags)flags,
                    Socket = conn,
                    IsListening = false,
                };

                mem.WriteInt32(ro_fd, (int)newFd);
                return (int)ErrNo.Success;
            }
            catch (SocketException) { return (int)ErrNo.IO; }
            catch (ObjectDisposedException) { return (int)ErrNo.Badf; }
        }

        [WacsImport("wasi_snapshot_preview1", "sock_recv")]
        public static int SockRecvCore(WacsHostMemory mem, State state, int sock,
            int ri_data, int ri_datalen, int ri_flags, int ro_data_len, int ro_flags)
        {
            if (!state.FileDescriptors.TryGetValue((uint)sock, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream &&
                fileDescriptor.Type != Filetype.SocketDgram)
                return (int)ErrNo.NotSock;
            if (fileDescriptor.Socket is null || fileDescriptor.IsListening)
                return (int)ErrNo.Inval;

            var iovs = mem.ReadStructs<IoVec>(ri_data, ri_datalen);
            var sockFlags = SocketFlags.None;
            if (((RiFlags)ri_flags & RiFlags.RecvPeek) != 0) sockFlags |= SocketFlags.Peek;

            int totalRead = 0;
            try
            {
                foreach (var iov in iovs)
                {
                    if (iov.bufLen == 0) continue;
                    var dest = new byte[iov.bufLen];
                    if (((RiFlags)ri_flags & RiFlags.RecvWaitAll) != 0)
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
                            mem.AsSpan((int)iov.bufPtr, got));
                        totalRead += got;
                        if (got < iov.bufLen) break;
                    }
                    else
                    {
                        int n = fileDescriptor.Socket.Receive(dest, 0, dest.Length, sockFlags);
                        new ReadOnlySpan<byte>(dest, 0, n).CopyTo(
                            mem.AsSpan((int)iov.bufPtr, n));
                        totalRead += n;
                        if (n < iov.bufLen) break;
                    }
                }
                mem.WriteInt32(ro_data_len, totalRead);
                mem.WriteInt32(ro_flags, 0);
                return (int)ErrNo.Success;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                { return (int)ErrNo.Again; }
            catch (SocketException) { return (int)ErrNo.IO; }
            catch (ObjectDisposedException) { return (int)ErrNo.Badf; }
        }

        [WacsImport("wasi_snapshot_preview1", "sock_send")]
        public static int SockSendCore(WacsHostMemory mem, State state, int sock,
            int si_data, int si_data_len, int si_flags, int ret_data_len)
        {
            if (!state.FileDescriptors.TryGetValue((uint)sock, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream &&
                fileDescriptor.Type != Filetype.SocketDgram)
                return (int)ErrNo.NotSock;
            if (fileDescriptor.Socket is null || fileDescriptor.IsListening)
                return (int)ErrNo.Inval;

            // si_flags is reserved in WASI Preview 1 (no defined bits); accept any.
            var iovs = mem.ReadStructs<IoVec>(si_data, si_data_len);
            int totalSent = 0;
            try
            {
                foreach (var iov in iovs)
                {
                    if (iov.bufLen == 0) continue;
                    var buf = mem.AsSpan((int)iov.bufPtr, (int)iov.bufLen).ToArray();
                    int n = fileDescriptor.Socket.Send(buf, 0, buf.Length, SocketFlags.None);
                    totalSent += n;
                    if (n < iov.bufLen) break;
                }
                mem.WriteInt32(ret_data_len, totalSent);
                return (int)ErrNo.Success;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                { return (int)ErrNo.Again; }
            catch (SocketException) { return (int)ErrNo.IO; }
            catch (ObjectDisposedException) { return (int)ErrNo.Badf; }
        }

        [WacsImport("wasi_snapshot_preview1", "sock_shutdown")]
        public static int SockShutdownCore(State state, int sock, int how)
        {
            if (!state.FileDescriptors.TryGetValue((uint)sock, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.SocketStream &&
                fileDescriptor.Type != Filetype.SocketDgram)
                return (int)ErrNo.NotSock;
            if (fileDescriptor.Socket is null)
                return (int)ErrNo.NotConn;

            SocketShutdown dir;
            var sd = (SdFlags)how;
            if ((sd & SdFlags.Rd) != 0 && (sd & SdFlags.Wr) != 0) dir = SocketShutdown.Both;
            else if ((sd & SdFlags.Rd) != 0) dir = SocketShutdown.Receive;
            else if ((sd & SdFlags.Wr) != 0) dir = SocketShutdown.Send;
            else return (int)ErrNo.Inval;

            try
            {
                fileDescriptor.Socket.Shutdown(dir);
                return (int)ErrNo.Success;
            }
            catch (SocketException) { return (int)ErrNo.IO; }
            catch (ObjectDisposedException) { return (int)ErrNo.Badf; }
        }
    }
}
