// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FluentValidation;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class FieldType
    {
        public readonly Mutability Mut;
        public readonly ValType StorageType; //ValType includes PackedType defs

        public FieldType(ValType st, Mutability mut)
        {
            Mut = mut;
            StorageType = st;
        }
        
        public static FieldType Parse(BinaryReader reader) => 
            new(
                ValTypeParser.Parse(reader, parseBlockIndex: false, parseStorageType: true),
                MutabilityParser.Parse(reader)
            );

        public bool Matches(FieldType other, TypesSpace? types)
        {
            if (!StorageType.Matches(other.StorageType, types))
                return false;
            if (Mut != other.Mut)
                return false;
            //const, const
            if (Mut == Mutability.Immutable && other.Mut == Mutability.Immutable)
                return true;
            //var,var case matches reciprocally
            return other.StorageType.Matches(StorageType, types);
        }

        public ValType UnpackType() => StorageType switch {
            ValType.I8 => ValType.I32,
            ValType.I16 => ValType.I32,
            _ => StorageType
        };

        public Value UnpackValue(ReadOnlySpan<byte> bytes)
        {
            switch (StorageType)
            {
                case ValType.I8:
                    return new Value(ValType.I32, (int)(sbyte)bytes[0]);
                case ValType.I16:
                    return new Value(ValType.I32, (int)BitConverter.ToInt16(bytes));
                case ValType.I32:
                    return new Value(ValType.I32, BitConverter.ToUInt32(bytes));
                case ValType.I64:
                    return new Value(ValType.I64, BitConverter.ToInt64(bytes));
                case ValType.F32:
                    return new Value(ValType.F32, BitConverter.ToSingle(bytes));
                case ValType.F64:
                    return new Value(ValType.F64, BitConverter.ToDouble(bytes));
                case ValType.V128:
#if NET8_0_OR_GREATER
                    var vec = MemoryMarshal.AsRef<V128>(bytes);
#else
                    var vec = MemoryMarshal.Read<V128>(bytes);
#endif
                    return new Value(ValType.V128, vec);
                default:
                    throw new WasmRuntimeException($"Cannot unpack byte sequence for fieldtype:{StorageType}");
            }
        }

        public BitWidth BitWidth() => StorageType switch
        {
            ValType.I8 => Types.BitWidth.U8,
            ValType.I16 => Types.BitWidth.U16,
            ValType.I32 or ValType.F32 => Types.BitWidth.U32,
            ValType.I64 or ValType.U64 => Types.BitWidth.U64,
            ValType.V128 => Types.BitWidth.V128,
            _ => Types.BitWidth.None,
        };
        
        public bool ValidExtension(PackedExt pt) => pt switch
        {
            PackedExt.NotPacked => StorageType is not (ValType.I8 or ValType.I16),
            PackedExt.Signed or PackedExt.Unsigned => StorageType is ValType.I8 or ValType.I16,
            _ => false
        };

        public class Validator : AbstractValidator<FieldType>
        {
            public Validator()
            {
                //PackedTypes are always valid
                RuleFor(ft => ft.StorageType)
                    .IsInEnum()
                    .When(ft => ft.StorageType.IsPacked());
                
                //StorageType
                RuleFor(ft => ft.StorageType)
                    .Must((_, vt, ctx) => vt.Validate(ctx.GetValidationContext().Types))
                    .When(ft => !ft.StorageType.IsPacked())
                    .WithMessage(ft => $"FieldType had invalid StorageType:{ft.StorageType}");
                
                //Spec ignores Mutability for validation
            }
        }

        public int ComputeHash(int defIndexValue, List<DefType> defs)
        {
            var hash = new StableHash();
            hash.Add(nameof(FieldType));
            hash.Add(Mut);
            hash.Add(StorageType.ComputeHash(defIndexValue,defs));
            return hash.ToHashCode();
        }
    }
}