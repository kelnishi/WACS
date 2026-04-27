// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    public sealed partial class SocketsBindings
    {
        // wasi:sockets/ip-name-lookup@0.2.3
        //   resolve-addresses: func(network: borrow<network>, name: string)
        //     -> result<own<resolve-address-stream>, error-code>
        // resource methods on resolve-address-stream:
        //   subscribe / resolve-next-address / resource-drop
        private static void BindIpNameLookup(WasmRuntime runtime,
            ResourceContext resources, IIpNameLookup? impl)
        {
            var streams = resources.Table<ResolveAddressStream>();
            var nets = resources.Table<Network>();
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (IpNameLookupNs, "[resource-drop]resolve-address-stream"),
                (_, h) => streams.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (IpNameLookupNs, "[method]resolve-address-stream.subscribe"),
                (_, h) => pollables.Allocate(
                    ((ResolveAddressStream)streams.Get(h)).Subscribe()));

            // resolve-next-address() ->
            //   result<option<ip-address>, error-code>.
            // ip-address variant {ipv4(u8×4), ipv6(u16×8)} —
            // align 2, size 18 (1 disc + 1 pad + 16 max payload).
            // option layout: 2 (disc + pad) + 18 = 20 bytes,
            // align 2.
            // result wrapper: 2 (disc + pad) + 20 = 22 bytes,
            // align 2.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (IpNameLookupNs,
                 "[method]resolve-address-stream.resolve-next-address"),
                (ctx, h, retArea) =>
                {
                    var item = ((ResolveAddressStream)streams.Get(h))
                        .ResolveNextAddress();
                    var mem = ctx.Memory();
                    mem[retArea] = 0;       // outer Ok
                    mem[retArea + 1] = 0;   // pad
                    if (item == null)
                    {
                        mem[retArea + 2] = 0;     // option None
                        for (int i = 3; i < 22; i++)
                            mem[retArea + i] = 0;
                        return;
                    }
                    mem[retArea + 2] = 1;       // option Some
                    mem[retArea + 3] = 0;
                    if (item is Ipv4Address v4)
                    {
                        mem[retArea + 4] = 0;   // variant ipv4
                        mem[retArea + 5] = 0;
                        mem[retArea + 6] = v4.Address[0];
                        mem[retArea + 7] = v4.Address[1];
                        mem[retArea + 8] = v4.Address[2];
                        mem[retArea + 9] = v4.Address[3];
                        for (int i = 10; i < 22; i++)
                            mem[retArea + i] = 0;
                    }
                    else
                    {
                        var v6 = (Ipv6Address)item;
                        mem[retArea + 4] = 1;   // variant ipv6
                        mem[retArea + 5] = 0;
                        for (int k = 0; k < 8; k++)
                            MemoryWriter.WriteU16LE(mem,
                                retArea + 6 + k * 2, v6.Address[k]);
                    }
                });

            if (impl == null) return;

            var alloc = new Realloc(runtime);

            // resolve-addresses(net: borrow<network>, name: string)
            //   -> result<own<resolve-address-stream>, error-code>
            // Wire: handle + (string ptr, string len) + retArea.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (IpNameLookupNs, "resolve-addresses"),
                (ctx, hNet, namePtr, nameLen, retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var stream = impl.ResolveAddresses(
                        (Network)nets.Get(hNet), name);
                    WriteOkHandle(ctx.Memory(), retArea,
                        streams.Allocate(stream));
                });
        }
    }
}
