using System;
using System.Security.Cryptography;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using ptr = System.Int32;
using size = System.Int32;

namespace Wacs.WASIp1
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
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains(bufOffsetPtr, bufLen))
                return ErrNo.Fault;
            
            var buf = mem[bufOffsetPtr..(bufOffsetPtr + bufLen)];
            
            // Fill the span with cryptographically secure random data.
            RandomNumberGenerator.Fill(buf);

            return ErrNo.Success;
        }
    }
}