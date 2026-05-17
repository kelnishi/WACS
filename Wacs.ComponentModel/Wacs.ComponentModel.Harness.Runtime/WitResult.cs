// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Harness
{
    /// <summary>
    /// CLR shape for WIT <c>result&lt;TOk, TErr&gt;</c>. Either side may
    /// be <see cref="System.ValueTuple"/> when the corresponding WIT
    /// side is elided (<c>result</c>, <c>result&lt;T&gt;</c>,
    /// <c>result&lt;_, E&gt;</c>) — the empty struct stands in for
    /// "no payload" without forcing a separate reference-type sentinel.
    /// </summary>
    public readonly struct WitResult<TOk, TErr>
    {
        public bool IsOk { get; }
        public TOk OkValue { get; }
        public TErr ErrValue { get; }

        private WitResult(bool isOk, TOk ok, TErr err)
        {
            IsOk = isOk;
            OkValue = ok;
            ErrValue = err;
        }

        public static WitResult<TOk, TErr> Ok(TOk value) =>
            new WitResult<TOk, TErr>(true, value, default!);

        public static WitResult<TOk, TErr> Err(TErr value) =>
            new WitResult<TOk, TErr>(false, default!, value);

        public override string ToString() =>
            IsOk ? $"Ok({OkValue})" : $"Err({ErrValue})";
    }
}
