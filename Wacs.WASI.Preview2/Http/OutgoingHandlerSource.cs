// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>Default <see cref="IOutgoingHandler"/> impl
    /// — returns a fresh stub
    /// <see cref="FutureIncomingResponse"/> regardless of
    /// input. Concrete hosts override to plumb through
    /// <c>System.Net.Http.HttpClient</c> or similar.</summary>
    public sealed class OutgoingHandlerSource : IOutgoingHandler
    {
        public Result<IFutureIncomingResponse, ErrorCode> Handle(
            IOutgoingRequest request, Option<IRequestOptions> options)
            => Result<IFutureIncomingResponse, ErrorCode>.FromOk(
                new FutureIncomingResponse());
    }
}
