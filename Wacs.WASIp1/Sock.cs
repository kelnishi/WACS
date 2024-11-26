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
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;
using fd = System.UInt32;
using ptr = System.UInt32;
using size = System.UInt32;

namespace Wacs.WASIp1
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

        public ErrNo SockAccept(ExecContext ctx, fd sock,
            FdFlags flags, ptr ro_fd)
        {
            return ErrNo.NotSup;
        }

        public ErrNo SockRecv(ExecContext ctx, fd sock,
            ptr ri_data, size ri_datalen, RiFlags ri_flags,
            ptr ro_data_len, ptr ro_flags)
        {
            return ErrNo.NotSup;
        }

        public ErrNo SockSend(ExecContext ctx, fd sock, 
            ptr si_data, size si_data_len, SiFlags si_flags,
            ptr ret_data_len)
        {
            return ErrNo.NotSup;
        }

        public ErrNo SockShutdown(ExecContext ctx, fd sock, SdFlags how)
        {
            return ErrNo.NotSup;
        }
        
    }
}