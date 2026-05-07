// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN.Types
{
    /// <summary>
    /// WIT <c>errors.error-code</c> enum
    /// (wasi:nn@0.2.0-rc-2024-10-28). The legacy WITX
    /// <c>nn_errno</c> uses different discriminants and
    /// includes <c>missing_memory</c> / <c>busy</c> cases
    /// that don't appear here; the WITX binding maps between
    /// the two enums explicitly.
    /// </summary>
    public enum ErrorCode : byte
    {
        InvalidArgument = 0,
        InvalidEncoding = 1,
        Timeout = 2,
        RuntimeError = 3,
        UnsupportedOperation = 4,
        TooLarge = 5,
        NotFound = 6,
        Security = 7,
        Unknown = 8,
    }

    /// <summary>
    /// Discriminants from the WITX <c>nn_errno</c> enum, in
    /// declaration order (success first). Used by the WITX
    /// binding to translate <see cref="WasiNNException"/>
    /// instances back into the legacy ABI's u16 status return.
    /// </summary>
    public enum WitxErrno : ushort
    {
        Success = 0,
        InvalidArgument = 1,
        InvalidEncoding = 2,
        MissingMemory = 3,
        Busy = 4,
        RuntimeError = 5,
        UnsupportedOperation = 6,
        TooLarge = 7,
        NotFound = 8,
    }

    /// <summary>
    /// Bidirectional mapping between WIT <see cref="ErrorCode"/>
    /// and legacy WITX <see cref="WitxErrno"/>. Cases not present
    /// in the destination enum collapse to the closest neighbor
    /// (WIT lacks <c>missing-memory</c>/<c>busy</c>; WITX lacks
    /// <c>security</c>/<c>unknown</c>).
    /// </summary>
    public static class ErrorCodeMapping
    {
        public static WitxErrno ToWitx(this ErrorCode code) => code switch
        {
            ErrorCode.InvalidArgument      => WitxErrno.InvalidArgument,
            ErrorCode.InvalidEncoding      => WitxErrno.InvalidEncoding,
            ErrorCode.Timeout              => WitxErrno.RuntimeError,
            ErrorCode.RuntimeError         => WitxErrno.RuntimeError,
            ErrorCode.UnsupportedOperation => WitxErrno.UnsupportedOperation,
            ErrorCode.TooLarge             => WitxErrno.TooLarge,
            ErrorCode.NotFound             => WitxErrno.NotFound,
            ErrorCode.Security             => WitxErrno.RuntimeError,
            ErrorCode.Unknown              => WitxErrno.RuntimeError,
            _                              => WitxErrno.RuntimeError,
        };

        public static ErrorCode ToWit(this WitxErrno code) => code switch
        {
            WitxErrno.Success              => ErrorCode.Unknown, // shouldn't be raised
            WitxErrno.InvalidArgument      => ErrorCode.InvalidArgument,
            WitxErrno.InvalidEncoding      => ErrorCode.InvalidEncoding,
            WitxErrno.MissingMemory        => ErrorCode.RuntimeError,
            WitxErrno.Busy                 => ErrorCode.RuntimeError,
            WitxErrno.RuntimeError         => ErrorCode.RuntimeError,
            WitxErrno.UnsupportedOperation => ErrorCode.UnsupportedOperation,
            WitxErrno.TooLarge             => ErrorCode.TooLarge,
            WitxErrno.NotFound             => ErrorCode.NotFound,
            _                              => ErrorCode.Unknown,
        };
    }
}
