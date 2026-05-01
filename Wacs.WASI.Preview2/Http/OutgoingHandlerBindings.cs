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
    /// On the Err path the binder writes outer disc=1 with the
    /// remaining bytes zeroed (no error-code payload encoded
    /// since the fixture's wit doesn't reserve room for one).
    /// </item>
    /// <item>Spec (<paramref name="useSpecErrorCode"/>=true):
    /// 40 bytes, align 8 — 1B Ok-disc + 7B pad + Ok-handle at
    /// +8 (with 28B trailing zero-fill). The Err path writes
    /// disc=1 + the 32-byte canon-encoded error-code variant
    /// via <see cref="ErrorCodeEncoder"/> at retArea+8.</item>
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

            // handle(req, option<options>) ->
            //   result<own<future-incoming-response>, error-code>
            // Wire: handle(req) + optDisc + handle(opts) + retArea
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "handle"),
                (ctx, hReq, optDisc, hOpts, retArea) =>
                {
                    var req = (OutgoingRequest)requests.Get(hReq);
                    Option<IRequestOptions> opts = optDisc == 0
                        ? Option<IRequestOptions>.None
                        : Option<IRequestOptions>.Some(
                            (RequestOptions)options.Get(hOpts));

                    var result = _impl.Handle(req, opts);
                    var mem = ctx.Memory();

                    if (result.IsOk)
                    {
                        // Ok path — allocate the future handle
                        // and write it at the appropriate offset.
                        int handle = futures.Allocate(
                            (FutureIncomingResponse)result.Ok);
                        if (spec)
                        {
                            // Spec layout: disc@+0, pad@+1..+7,
                            // handle@+8, zero@+12..+39.
                            mem[retArea] = 0;
                            for (int p = 1; p < 8; p++)
                                mem[retArea + p] = 0;
                            MemoryWriter.WriteI32LE(mem,
                                retArea + 8, handle);
                            for (int p = 12; p < 40; p++)
                                mem[retArea + p] = 0;
                        }
                        else
                        {
                            // Simplified: disc@+0, pad@+1..+3,
                            // handle@+4.
                            mem[retArea] = 0;
                            mem[retArea + 1] = 0;
                            mem[retArea + 2] = 0;
                            mem[retArea + 3] = 0;
                            MemoryWriter.WriteI32LE(mem,
                                retArea + 4, handle);
                        }
                        return;
                    }

                    // Err path
                    if (spec)
                    {
                        mem[retArea] = 1;
                        for (int p = 1; p < 8; p++)
                            mem[retArea + p] = 0;
                        ErrorCodeEncoder.Write(mem, retArea + 8,
                            result.Err, alloc.Allocate);
                    }
                    else
                    {
                        // Simplified: outer disc=1 only.
                        mem[retArea] = 1;
                        for (int p = 1; p < 8; p++)
                            mem[retArea + p] = 0;
                    }
                });
        }
    }
}
