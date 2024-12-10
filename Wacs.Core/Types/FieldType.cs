// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System.Collections.Generic;
using System.IO;
using FluentValidation;
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