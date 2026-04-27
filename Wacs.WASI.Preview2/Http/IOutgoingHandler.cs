// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>Host-side surface for
    /// <c>wasi:http/outgoing-handler.handle</c>:
    /// <code>handle: func(
    ///     request: own&lt;outgoing-request&gt;,
    ///     options: option&lt;own&lt;request-options&gt;&gt;,
    /// ) -&gt; result&lt;own&lt;future-incoming-response&gt;,
    ///                 error-code&gt;;</code>
    /// The dispatch entrypoint every HTTP-client guest
    /// reaches for to send a request.</summary>
    public interface IOutgoingHandler
    {
        FutureIncomingResponse Handle(OutgoingRequest request,
            RequestOptions? options);
    }

    /// <summary>Default <see cref="IOutgoingHandler"/> impl
    /// — returns a fresh stub
    /// <see cref="FutureIncomingResponse"/> regardless of
    /// input. Concrete hosts override to plumb through
    /// <c>System.Net.Http.HttpClient</c> or similar.</summary>
    public sealed class OutgoingHandlerSource : IOutgoingHandler
    {
        public FutureIncomingResponse Handle(OutgoingRequest request,
            RequestOptions? options)
            => new FutureIncomingResponse();
    }
}
