// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    public sealed partial class SocketsBindings
    {
        // wasi:sockets/ip-name-lookup@0.2.8
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
                    (Pollable)((ResolveAddressStream)streams.Get(h))
                        .Subscribe()));

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
                    var r = ((ResolveAddressStream)streams.Get(h))
                        .ResolveNextAddress();
                    var mem = ctx.Memory();
                    if (!r.IsOk)
                    {
                        WriteErrCode(mem, retArea, 22, r.Err);
                        return;
                    }
                    mem[retArea] = 0;       // outer Ok
                    mem[retArea + 1] = 0;   // pad
                    var item = r.Ok;
                    if (!item.HasValue)
                    {
                        mem[retArea + 2] = 0;     // option None
                        for (int i = 3; i < 22; i++)
                            mem[retArea + i] = 0;
                        return;
                    }
                    mem[retArea + 2] = 1;       // option Some
                    mem[retArea + 3] = 0;
                    if (item.Value is IpAddress.IpAddressIpv4 v4Case)
                    {
                        var v4 = v4Case.Value;
                        mem[retArea + 4] = 0;   // variant ipv4
                        mem[retArea + 5] = 0;
                        mem[retArea + 6] = v4.Item1;
                        mem[retArea + 7] = v4.Item2;
                        mem[retArea + 8] = v4.Item3;
                        mem[retArea + 9] = v4.Item4;
                        for (int i = 10; i < 22; i++)
                            mem[retArea + i] = 0;
                    }
                    else
                    {
                        var v6 = ((IpAddress.IpAddressIpv6)item.Value).Value;
                        mem[retArea + 4] = 1;   // variant ipv6
                        mem[retArea + 5] = 0;
                        MemoryWriter.WriteU16LE(mem, retArea + 6, v6.Item1);
                        MemoryWriter.WriteU16LE(mem, retArea + 8, v6.Item2);
                        MemoryWriter.WriteU16LE(mem, retArea + 10, v6.Item3);
                        MemoryWriter.WriteU16LE(mem, retArea + 12, v6.Item4);
                        MemoryWriter.WriteU16LE(mem, retArea + 14, v6.Item5);
                        MemoryWriter.WriteU16LE(mem, retArea + 16, v6.Item6);
                        MemoryWriter.WriteU16LE(mem, retArea + 18, v6.Item7);
                        MemoryWriter.WriteU16LE(mem, retArea + 20, v6.Item8);
                    }
                });

            if (impl == null) return;

            // resolve-addresses(net: borrow<network>, name: string)
            //   -> result<own<resolve-address-stream>, error-code>
            // Wire: handle + (string ptr, string len) + retArea.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (IpNameLookupNs, "resolve-addresses"),
                (ctx, hNet, namePtr, nameLen, retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var r = impl.ResolveAddresses(
                        (Network)nets.Get(hNet), name);
                    var handleResult = r.IsOk
                        ? Result<int, ErrorCode>.FromOk(
                            streams.Allocate((ResolveAddressStream)r.Ok))
                        : Result<int, ErrorCode>.FromErr(r.Err);
                    WriteResultHandle(ctx.Memory(), retArea, handleResult);
                });
        }
    }
}
