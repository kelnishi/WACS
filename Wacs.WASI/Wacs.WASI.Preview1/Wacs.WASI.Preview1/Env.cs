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
using System.Linq;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.HostBindings;
using ptr = System.Int32;
using size = System.UInt32;

namespace Wacs.WASI.Preview1
{
    /// <summary>
    /// Implements WASI environment variable and argument functions.
    /// </summary>
    public class Env : IBindable
    {
        private readonly WasiConfiguration _config;

        public Env(WasiConfiguration config)
        {
            _config = config;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "args_sizes_get"), ArgsSizesGet);
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "args_get"), ArgsGet);
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "environ_sizes_get"), EnvironSizesGet);
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "environ_get"), EnvironGet);
        }

        // Interpreter wrappers (unchanged binding contract)
        public ErrNo ArgsSizesGet(ExecContext ctx, ptr argcPtr, ptr argvBufSizePtr)
            => ArgsSizesGetCore(Clock.WacsHost(ctx), _config, argcPtr, argvBufSizePtr);

        public ErrNo ArgsGet(ExecContext ctx, ptr argvPtr, ptr argvBufPtr)
            => ArgsGetCore(Clock.WacsHost(ctx), _config, argvPtr, argvBufPtr);

        public ErrNo EnvironSizesGet(ExecContext ctx, ptr environCountPtr, ptr environBufSizePtr)
            => EnvironSizesGetCore(Clock.WacsHost(ctx), _config, environCountPtr, environBufSizePtr);

        public ErrNo EnvironGet(ExecContext ctx, ptr environPtr, ptr environBufPtr)
            => EnvironGetCore(Clock.WacsHost(ctx), _config, environPtr, environBufPtr);

        // ============================================================
        // AOT-friendly static entry points
        // ============================================================

        /// <summary>Counts and total UTF-8 buffer size for argv.</summary>
        [WacsImport("wasi_snapshot_preview1", "args_sizes_get")]
        public static ErrNo ArgsSizesGetCore(WacsHostMemory mem, WasiConfiguration config,
                                              ptr argcPtr, ptr argvBufSizePtr)
        {
            int argc = config.Arguments.Count;
            int argvBufSize = config.Arguments.Sum(arg => Encoding.UTF8.GetByteCount(arg) + 1);

            mem.WriteInt32LE(argcPtr, argc);
            mem.WriteInt32LE(argvBufSizePtr, argvBufSize);
            return ErrNo.Success;
        }

        /// <summary>Copy argv strings + pointer table into linear memory.</summary>
        [WacsImport("wasi_snapshot_preview1", "args_get")]
        public static ErrNo ArgsGetCore(WacsHostMemory mem, WasiConfiguration config,
                                         ptr argvPtr, ptr argvBufPtr)
        {
            foreach (string arg in config.Arguments)
            {
                int strLen = mem.WriteUtf8String(argvBufPtr, arg, nullTerminate: true);
                mem.WriteInt32LE(argvPtr, argvBufPtr);
                argvBufPtr += strLen;
                argvPtr += sizeof(ptr);
            }
            return ErrNo.Success;
        }

        /// <summary>Counts and total UTF-8 buffer size for environ.</summary>
        [WacsImport("wasi_snapshot_preview1", "environ_sizes_get")]
        public static ErrNo EnvironSizesGetCore(WacsHostMemory mem, WasiConfiguration config,
                                                 ptr environCountPtr, ptr environBufSizePtr)
        {
            int environCount = config.EnvironmentVariables.Count;
            int environBufSize = config.EnvironmentVariables.Sum(envVar =>
                Encoding.UTF8.GetByteCount($"{envVar.Key}={envVar.Value}") + 1);

            mem.WriteInt32LE(environCountPtr, environCount);
            mem.WriteInt32LE(environBufSizePtr, environBufSize);
            return ErrNo.Success;
        }

        /// <summary>Copy environ strings + pointer table into linear memory.</summary>
        [WacsImport("wasi_snapshot_preview1", "environ_get")]
        public static ErrNo EnvironGetCore(WacsHostMemory mem, WasiConfiguration config,
                                            ptr environPtr, ptr environBufPtr)
        {
            foreach (var envVar in config.EnvironmentVariables)
            {
                string envEntry = $"{envVar.Key}={envVar.Value}";
                int strLen = mem.WriteUtf8String(environBufPtr, envEntry, nullTerminate: true);
                mem.WriteInt32LE(environPtr, environBufPtr);
                environBufPtr += strLen;
                environPtr += sizeof(ptr);
            }
            return ErrNo.Success;
        }
    }
}