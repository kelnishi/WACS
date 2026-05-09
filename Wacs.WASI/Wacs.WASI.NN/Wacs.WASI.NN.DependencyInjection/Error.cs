// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Host representation of <c>wasi:nn/errors.error</c>. Carries
    /// a typed <see cref="Nn.ErrorCode"/> and a human-readable
    /// data string. Created by the host (never by the guest);
    /// returned through <c>Result&lt;_, error&gt;</c> branches on
    /// any wasi-nn host method that can fail.
    /// </summary>
    public sealed class Error : Nn.IError, IDisposable
    {
        private readonly Nn.ErrorCode _code;
        private readonly string _data;

        public Error(Nn.ErrorCode code, string data)
        {
            _code = code;
            _data = data ?? string.Empty;
        }

        /// <summary>
        /// Lift a <see cref="Types.WasiNNException"/> from the
        /// backend SPI into a wasi-nn error resource. Maps the
        /// typed Code by ordinal (the source-gen and hand-written
        /// enums share WIT order) and uses <c>BackendData</c> when
        /// present, falling back to <see cref="Exception.Message"/>.
        /// </summary>
        public static Error FromException(Types.WasiNNException ex)
        {
            return new Error((Nn.ErrorCode)(int)ex.Code,
                ex.BackendData ?? ex.Message);
        }

        public Nn.ErrorCode Code() => _code;
        public string Data() => _data;

        public void Dispose() { }
    }
}
