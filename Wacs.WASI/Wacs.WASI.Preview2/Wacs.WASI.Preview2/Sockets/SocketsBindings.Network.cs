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
        // wasi:sockets/network@0.2.8 — Network resource (no
        // methods) plus a top-level function:
        //   network-error-code: func(err: borrow<error>)
        //     -> option<error-code>
        // The default impl returns None — i.e. no Error
        // resource is treated as a network-related error code.
        // Hosts that want to classify subclass SocketsBindings
        // and override this binding.
        private static void BindNetwork(WasmRuntime runtime,
            ResourceContext resources)
        {
            var nets = resources.Table<Network>();
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (NetworkNs, "[resource-drop]network"),
                (_, h) => nets.Drop(h));

            // network-error-code(err: borrow<error>)
            //   -> option<error-code>
            // Wire: handle (i32) + retArea (i32) -> void.
            // retArea = 2 bytes: option disc (u8) + error-code
            // (u8); align 1 since both are u8.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (NetworkNs, "network-error-code"),
                (ctx, errHandle, retArea) =>
                {
                    // Default: no classification — return None.
                    // The errHandle borrow is informational; we
                    // don't dereference it in the default impl.
                    var mem = ctx.Memory();
                    mem[retArea] = 0;       // option None
                    mem[retArea + 1] = 0;   // payload zeroed
                });
        }

        // wasi:sockets/instance-network@0.2.8
        //   instance-network: func() -> own<network>
        private static void BindInstanceNetwork(WasmRuntime runtime,
            ResourceContext resources, IInstanceNetwork impl)
        {
            var nets = resources.Table<Network>();
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (InstanceNs, "instance-network"),
                _ => nets.Allocate((Network)impl.InstanceNetwork()));
        }

        // wasi:sockets/tcp-create-socket@0.2.8
        //   create-tcp-socket: func(address-family: ip-address-family)
        //     -> result<own<tcp-socket>, error-code>
        // Result-returning host method: allocate the handle on Ok,
        // propagate the ErrorCode on Err.
        private static void BindTcpCreateSocket(WasmRuntime runtime,
            ResourceContext resources, ITcpCreateSocket impl)
        {
            var socks = resources.Table<TcpSocket>();
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TcpCreateNs, "create-tcp-socket"),
                (ctx, family, retArea) =>
                {
                    var r = impl.CreateTcpSocket(
                        (IpAddressFamily)family);
                    var handleResult = r.IsOk
                        ? Wacs.ComponentModel.Runtime.Result<int, ErrorCode>
                            .FromOk(socks.Allocate((TcpSocket)r.Ok))
                        : Wacs.ComponentModel.Runtime.Result<int, ErrorCode>
                            .FromErr(r.Err);
                    WriteResultHandle(ctx.Memory(), retArea, handleResult);
                });
        }

        // wasi:sockets/udp-create-socket@0.2.8
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
                    var r = impl.CreateUdpSocket(
                        (IpAddressFamily)family);
                    var handleResult = r.IsOk
                        ? Wacs.ComponentModel.Runtime.Result<int, ErrorCode>
                            .FromOk(socks.Allocate((UdpSocket)r.Ok))
                        : Wacs.ComponentModel.Runtime.Result<int, ErrorCode>
                            .FromErr(r.Err);
                    WriteResultHandle(ctx.Memory(), retArea, handleResult);
                });
        }
    }
}
