// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview3.Http
{
    /// <summary>
    /// Host interface for the WIT
    /// <c>resource request-options</c>.
    /// Per-request timeout configuration. All durations are
    /// nanoseconds (WIT <c>duration</c> alias for u64 ns); the
    /// CLR side uses nullable u64 to represent the WIT
    /// <c>option&lt;duration&gt;</c>.
    /// </summary>
    public interface IRequestOptions
    {
        ulong? GetConnectTimeout();
        void SetConnectTimeout(ulong? durationNanoseconds);
        ulong? GetFirstByteTimeout();
        void SetFirstByteTimeout(ulong? durationNanoseconds);
        ulong? GetBetweenBytesTimeout();
        void SetBetweenBytesTimeout(ulong? durationNanoseconds);
        IRequestOptions Clone();
    }

    /// <summary>
    /// Default in-memory <see cref="IRequestOptions"/>
    /// implementation. Constructed-fresh instances are mutable;
    /// <see cref="Freeze"/> makes them immutable (matching the
    /// fields-immutability pattern), and any subsequent setter
    /// throws <see cref="RequestOptionsException"/> with
    /// <see cref="RequestOptionsError.Immutable"/>.
    /// </summary>
    public sealed class RequestOptions : IRequestOptions
    {
        private ulong? _connect;
        private ulong? _firstByte;
        private ulong? _betweenBytes;
        private bool _frozen;

        public bool IsFrozen => _frozen;

        public ulong? GetConnectTimeout() => _connect;
        public void SetConnectTimeout(ulong? duration)
        {
            EnsureMutable();
            _connect = duration;
        }

        public ulong? GetFirstByteTimeout() => _firstByte;
        public void SetFirstByteTimeout(ulong? duration)
        {
            EnsureMutable();
            _firstByte = duration;
        }

        public ulong? GetBetweenBytesTimeout() => _betweenBytes;
        public void SetBetweenBytesTimeout(ulong? duration)
        {
            EnsureMutable();
            _betweenBytes = duration;
        }

        public IRequestOptions Clone() => new RequestOptions
        {
            _connect = _connect,
            _firstByte = _firstByte,
            _betweenBytes = _betweenBytes,
        };

        public void Freeze() => _frozen = true;

        private void EnsureMutable()
        {
            if (_frozen)
                throw new RequestOptionsException(
                    RequestOptionsError.Immutable,
                    "request-options is frozen; no mutation allowed.");
        }
    }
}
