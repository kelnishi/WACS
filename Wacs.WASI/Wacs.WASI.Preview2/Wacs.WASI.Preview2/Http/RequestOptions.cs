// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.request-options</c>
    /// — per-request transport tunables (timeouts etc.).
    /// All timeouts are option<duration> = option<u64>;
    /// null means "no timeout".</summary>
    [WasiResource("request-options")]
    public class RequestOptions : IRequestOptions, IDisposable
    {
        protected ulong? _connectTimeout;
        protected ulong? _firstByteTimeout;
        protected ulong? _betweenBytesTimeout;

        /// <summary>WIT <c>constructor()</c> — zero-arg
        /// factory.</summary>
        public static RequestOptions New() => new RequestOptions();

        public virtual void Create() { }

        public virtual Option<ulong> ConnectTimeout()
            => _connectTimeout.HasValue
                ? Option<ulong>.Some(_connectTimeout.Value)
                : Option<ulong>.None;

        /// <summary>WIT <c>set-connect-timeout: func(
        ///   duration: option&lt;duration&gt;)
        ///   -&gt; result</c>.</summary>
        public virtual Result<Unit, Unit> SetConnectTimeout(
            Option<ulong> duration)
        {
            _connectTimeout = duration.HasValue ? duration.Value
                : (ulong?)null;
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        public virtual Option<ulong> FirstByteTimeout()
            => _firstByteTimeout.HasValue
                ? Option<ulong>.Some(_firstByteTimeout.Value)
                : Option<ulong>.None;

        public virtual Result<Unit, Unit> SetFirstByteTimeout(
            Option<ulong> duration)
        {
            _firstByteTimeout = duration.HasValue ? duration.Value
                : (ulong?)null;
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        public virtual Option<ulong> BetweenBytesTimeout()
            => _betweenBytesTimeout.HasValue
                ? Option<ulong>.Some(_betweenBytesTimeout.Value)
                : Option<ulong>.None;

        public virtual Result<Unit, Unit> SetBetweenBytesTimeout(
            Option<ulong> duration)
        {
            _betweenBytesTimeout = duration.HasValue ? duration.Value
                : (ulong?)null;
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        public virtual void Dispose() { }
    }
}
