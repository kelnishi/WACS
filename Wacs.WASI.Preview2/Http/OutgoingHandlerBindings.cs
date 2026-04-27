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

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>
    /// Bindings for <c>wasi:http/outgoing-handler@0.2.3</c>:
    /// <code>handle: func(
    ///     request: own&lt;outgoing-request&gt;,
    ///     options: option&lt;own&lt;request-options&gt;&gt;,
    /// ) -&gt; result&lt;own&lt;future-incoming-response&gt;,
    ///                 error-code&gt;;</code>
    ///
    /// <para>Two retArea shapes — picked at construction time:
    /// <list type="bullet">
    /// <item>Simplified (default): 8 bytes — 1B Ok-disc + 3B
    /// pad + 4B handle. Used by every fixture whose WIT
    /// declares a single-case <c>error-code</c> placeholder.
    /// Throws from the host propagate as traps; the Err side
    /// isn't exercised.</item>
    /// <item>Spec (<paramref name="useSpecErrorCode"/>=true):
    /// 40 bytes, align 8 — 1B Ok-disc + 7B pad + Ok-handle at
    /// +8 (with 28B trailing zero-fill). The host can throw
    /// <see cref="WasiErrorCodeException"/> to write disc=1 +
    /// the 32-byte canon-encoded error-code variant via
    /// <see cref="ErrorCodeEncoder"/> at retArea+8.</item>
    /// </list></para>
    /// </summary>
    public sealed class OutgoingHandlerBindings : IBindable
    {
        private const string Ns = "wasi:http/outgoing-handler@0.2.3";

        private readonly ResourceContext _resources;
        private readonly IOutgoingHandler _impl;
        private readonly bool _useSpecErrorCode;

        public OutgoingHandlerBindings(ResourceContext resources,
            IOutgoingHandler impl, bool useSpecErrorCode = false)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _impl = impl
                ?? throw new ArgumentNullException(nameof(impl));
            _useSpecErrorCode = useSpecErrorCode;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var requests = _resources.Table<OutgoingRequest>();
            var options = _resources.Table<RequestOptions>();
            var futures = _resources.Table<FutureIncomingResponse>();
            var alloc = new Realloc(runtime);
            bool spec = _useSpecErrorCode;
            int okHandleOffset = spec ? 8 : 4;

            // handle(req, option<options>) ->
            //   result<own<future-incoming-response>, error-code>
            // Wire: handle(req) + optDisc + handle(opts) + retArea
            // = 4 ints + ExecContext.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "handle"),
                (ctx, hReq, optDisc, hOpts, retArea) =>
                {
                    var req = (OutgoingRequest)requests.Get(hReq);
                    RequestOptions? opts = optDisc == 0 ? null
                        : (RequestOptions)options.Get(hOpts);

                    FutureIncomingResponse result;
                    try
                    {
                        result = _impl.Handle(req, opts);
                    }
                    catch (WasiErrorCodeException wec) when (spec)
                    {
                        var memErr = ctx.Memory();
                        memErr[retArea] = 1;            // Err
                        for (int p = 1; p < 8; p++)
                            memErr[retArea + p] = 0;
                        ErrorCodeEncoder.Write(memErr,
                            retArea + 8, wec.Code,
                            alloc.Allocate);
                        return;
                    }

                    int handle = futures.Allocate(result);
                    var mem = ctx.Memory();
                    mem[retArea] = 0;                   // Ok
                    for (int p = 1; p < okHandleOffset; p++)
                        mem[retArea + p] = 0;
                    MemoryWriter.WriteI32LE(mem,
                        retArea + okHandleOffset, handle);
                    if (spec)
                    {
                        // Zero the trailing variant-payload area
                        // so the guest doesn't see stale Err
                        // bytes from a prior call.
                        for (int p = okHandleOffset + 4;
                             p < 40; p++)
                            mem[retArea + p] = 0;
                    }
                });
        }
    }
}
