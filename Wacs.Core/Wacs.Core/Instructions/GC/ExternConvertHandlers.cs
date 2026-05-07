// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.GC
{
    /// <summary>
    /// [OpHandler] entries for the three extern-conversion ops and ref.eq. No
    /// immediates — they inspect the incoming ref's concrete payload type and
    /// re-tag. Bodies mirror InstAnyConvertExtern / InstExternConvertAny /
    /// InstRefEq.
    /// </summary>
    internal static class ExternConvertHandlers
    {
        // 0xFB 0x1A any.convert_extern — Extern → Any (preserves nullability, detects
        // struct / array / i31 payloads so the dispatcher-visible type is meaningful).
        [OpHandler(GcCode.AnyConvertExtern)]
        private static Value AnyConvertExtern(Value refVal)
        {
            refVal.Type = refVal.GcRef switch
            {
                StoreStruct => refVal.Type.IsNullable() ? ValType.Struct : ValType.StructNN,
                StoreArray => refVal.Type.IsNullable() ? ValType.Array : ValType.ArrayNN,
                I31Ref => refVal.Type.IsNullable() ? ValType.I31 : ValType.I31NN,
                _ => refVal.Type.IsNullable() ? ValType.Any : ValType.Ref,
            };
            return refVal;
        }

        // 0xFB 0x1B extern.convert_any — Any → Extern (just re-tag).
        [OpHandler(GcCode.ExternConvertAny)]
        private static Value ExternConvertAny(Value refVal)
        {
            refVal.Type = refVal.Type.IsNullable() ? ValType.ExternRef : ValType.Extern;
            return refVal;
        }

    }
}
