using System;
using System.Security.Cryptography;
using Wacs.Core.Runtime;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public class Random : IBindable
    {
        public Random() {}


        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,int,int,ErrNo>>((module, "random_get"), RandomGet);
        }

        public ErrNo RandomGet(ExecContext ctx, int bufOffset, int bufLen)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains(bufOffset, bufLen))
                return ErrNo.Fault;
            
            var buf = mem[bufOffset..(bufOffset + bufLen)];
            
            // Fill the span with cryptographically secure random data.
            RandomNumberGenerator.Fill(buf);

            return ErrNo.Success;
        }
    }
}