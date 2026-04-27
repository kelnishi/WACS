// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.request-options</c>
    /// — per-request transport tunables (timeouts etc.).
    /// All timeouts are option<duration> = option<u64>;
    /// null means "no timeout".</summary>
    [WasiResource("request-options")]
    public class RequestOptions : IDisposable
    {
        protected ulong? _connectTimeout;
        protected ulong? _firstByteTimeout;
        protected ulong? _betweenBytesTimeout;

        /// <summary>WIT <c>constructor()</c> — zero-arg
        /// factory; guests call <c>[constructor]request-options</c>
        /// to get a fresh tunable bundle with all timeouts
        /// unset.</summary>
        [WasiConstructor]
        public static RequestOptions New() => new RequestOptions();

        [WasiOptionalReturn]
        [WasiMethodName("connect-timeout")]
        public virtual ulong? ConnectTimeout() => _connectTimeout;

        /// <summary>WIT <c>set-connect-timeout: func(
        ///   duration: option&lt;duration&gt;)
        ///   -&gt; result&lt;_, error-code&gt;</c>. duration is
        /// option<u64>; null clears the timeout.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-connect-timeout")]
        public virtual void SetConnectTimeout(
            [WasiOptionalParam] ulong? duration)
            => _connectTimeout = duration;

        [WasiOptionalReturn]
        [WasiMethodName("first-byte-timeout")]
        public virtual ulong? FirstByteTimeout() => _firstByteTimeout;

        /// <summary>WIT <c>set-first-byte-timeout</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-first-byte-timeout")]
        public virtual void SetFirstByteTimeout(
            [WasiOptionalParam] ulong? duration)
            => _firstByteTimeout = duration;

        [WasiOptionalReturn]
        [WasiMethodName("between-bytes-timeout")]
        public virtual ulong? BetweenBytesTimeout()
            => _betweenBytesTimeout;

        /// <summary>WIT <c>set-between-bytes-timeout</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-between-bytes-timeout")]
        public virtual void SetBetweenBytesTimeout(
            [WasiOptionalParam] ulong? duration)
            => _betweenBytesTimeout = duration;

        public virtual void Dispose() { }
    }
}
