// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.Resources
{
    /// <summary>
    /// WIT <c>error</c> resource. Wraps a
    /// <see cref="ErrorCode"/> + free-form data string —
    /// guests use <c>error.code()</c> for typed dispatch and
    /// <c>error.data()</c> for diagnostic messaging.
    ///
    /// <para>Constructed by the WIT binding layer when a
    /// backend throws <see cref="WasiNNException"/>; the host
    /// allocates a handle, lowers it into the result's Err
    /// branch, and the guest reads back the code/data through
    /// these accessors. Dropped by the guest's
    /// <c>[resource-drop]error</c> import.</para>
    /// </summary>
    internal sealed class ErrorResource
    {
        public ErrorCode Code { get; }
        public string Data { get; }

        public ErrorResource(ErrorCode code, string data)
        {
            Code = code;
            Data = data ?? string.Empty;
        }

        public ErrorResource(WasiNNException exc)
        {
            Code = exc.Code;
            Data = exc.BackendData ?? exc.Message ?? string.Empty;
        }
    }
}
