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
using System.Security.Cryptography;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.HostBindings;
using ptr = System.Int32;
using size = System.Int32;

namespace Wacs.WASI.Preview1
{
    public class Random : IBindable
    {
        public Random() {}


        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,ptr,size,ErrNo>>((module, "random_get"), RandomGet);
        }

        public ErrNo RandomGet(ExecContext ctx, ptr bufOffsetPtr, size bufLen)
            => RandomGetCore(Clock.WacsHost(ctx), bufOffsetPtr, bufLen);

        [WacsImport("wasi_snapshot_preview1", "random_get")]
        public static ErrNo RandomGetCore(WacsHostMemory mem, ptr bufOffsetPtr, size bufLen)
        {
            if (!mem.Contains(bufOffsetPtr, bufLen))
                return ErrNo.Fault;

            // Fill the span with cryptographically secure random data.
            RandomNumberGenerator.Fill(mem.AsSpan(bufOffsetPtr, bufLen));

            return ErrNo.Success;
        }
    }
}