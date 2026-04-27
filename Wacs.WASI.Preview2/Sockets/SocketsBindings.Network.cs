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

namespace Wacs.WASI.Preview2.Sockets
{
    public sealed partial class SocketsBindings
    {
        // wasi:sockets/network@0.2.3 — the resource carries no
        // methods; only the resource-drop is wired.
        private static void BindNetwork(WasmRuntime runtime,
            ResourceContext resources)
        {
            var nets = resources.Table<Network>();
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (NetworkNs, "[resource-drop]network"),
                (_, h) => nets.Drop(h));
        }

        // wasi:sockets/instance-network@0.2.3
        //   instance-network: func() -> own<network>
        private static void BindInstanceNetwork(WasmRuntime runtime,
            ResourceContext resources, IInstanceNetwork impl)
        {
            var nets = resources.Table<Network>();
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (InstanceNs, "instance-network"),
                _ => nets.Allocate(impl.InstanceNetwork()));
        }

        // wasi:sockets/tcp-create-socket@0.2.3
        //   create-tcp-socket: func(address-family: ip-address-family)
        //     -> result<own<tcp-socket>, error-code>
        private static void BindTcpCreateSocket(WasmRuntime runtime,
            ResourceContext resources, ITcpCreateSocket impl)
        {
            var socks = resources.Table<TcpSocket>();
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpCreateNs, "create-tcp-socket"),
                (ctx, family, retArea) =>
                {
                    var s = impl.CreateTcpSocket(
                        (IpAddressFamily)family);
                    WriteOkHandle(ctx.Memory(), retArea,
                        socks.Allocate(s));
                });
        }

        // wasi:sockets/udp-create-socket@0.2.3
        //   create-udp-socket: func(address-family: ip-address-family)
        //     -> result<own<udp-socket>, error-code>
        private static void BindUdpCreateSocket(WasmRuntime runtime,
            ResourceContext resources, IUdpCreateSocket impl)
        {
            var socks = resources.Table<UdpSocket>();
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (UdpCreateNs, "create-udp-socket"),
                (ctx, family, retArea) =>
                {
                    var s = impl.CreateUdpSocket(
                        (IpAddressFamily)family);
                    WriteOkHandle(ctx.Memory(), retArea,
                        socks.Allocate(s));
                });
        }
    }
}
