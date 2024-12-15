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

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class SubType
    {
        public readonly bool Final;
        public readonly TypeIdx[] SuperTypeIndexes;
        public readonly CompositeType Body;

        public SubType(TypeIdx[] idxs, CompositeType body, bool final)
        {
            SuperTypeIndexes = idxs;
            Body = body;
            Final = final;
        }
        
        public SubType(CompositeType body, bool final)
        {
            SuperTypeIndexes = Array.Empty<TypeIdx>();
            Body = body;
            Final = final;
            
            var hash = new StableHash();
            hash.Add(nameof(SubType));
            ComputedHash = hash.ToHashCode();
        }
        
        public static TypeIdx ParseTypeIndexes(BinaryReader reader) => 
            (TypeIdx)reader.ReadLeb128_u32();
        
        public static SubType Parse(BinaryReader reader)
        {
            return reader.ReadByte() switch
            {
                (byte)RecType.SubXCt => new SubType(
                    reader.ParseVector(ParseTypeIndexes),
                    CompositeType.ParseTagged(reader),
                    false
                ),
                (byte)RecType.SubFinalXCt => new SubType(
                    reader.ParseVector(ParseTypeIndexes),
                    CompositeType.ParseTagged(reader),
                    true
                ),
                (byte)CompType.ArrayAt => new SubType(ArrayType.Parse(reader), true),
                (byte)CompType.StructSt => new SubType(StructType.Parse(reader), true),
                (byte)CompType.FuncFt => new SubType(FunctionType.Parse(reader), true),
                
                var form => throw new FormatException(
                    $"Invalid type format {form} at offset {reader.BaseStream.Position-1}.")
            };
        }

        private int ComputedHash { get; set; }
        
        public void ComputeHash(int defIndexValue, List<DefType> defs)
        {
            var hash = new StableHash();
            hash.Add(nameof(SubType));
            hash.Add(Final);
            foreach (var super in SuperTypeIndexes)
            {
                if (super.Value >= defs.Count)
                    throw new FormatException($"SubTypes cannot forward-declare beyond the recursive block.");
                
                int superIndex = super.Value - defIndexValue;
                if (superIndex < 0)
                {
                    superIndex = defs[super.Value].GetHashCode();
                }
                hash.Add(superIndex);
            }
            hash.Add(Body.ComputeHash(defIndexValue, defs));
            ComputedHash = hash.ToHashCode();
        }

        public override int GetHashCode() => ComputedHash;

        public class Validator : AbstractValidator<SubType>
        {
            ///https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-subtypemathsfsubhrefsyntax-subtypemathsffinalyasthrefsyntax-comptypemathitcomptype
            public Validator(RecursiveType recType)
            {
                RuleFor(st => st.Body)
                    .SetValidator(new CompositeType.Validator());
                RuleFor(st => st.SuperTypeIndexes)
                    .Must(types => types.Length < 2)
                    .WithMessage("SubType can have at most 1 super type");
                RuleFor(st => st)
                    .Custom((sub, ctx) =>
                    {
                        var vContext = ctx.GetValidationContext();
                        var subIndex = (int)ctx.RootContextData["Index"];
                        var defIndex = recType.DefIndex.Value + subIndex;
                        var comptype = sub.Body;
                        
                        foreach (var y in sub.SuperTypeIndexes)
                        {
                            if (!vContext.Types.Contains(y))
                                throw new ValidationException($"SuperType {y} does not exist in the context");
                            var subtypeI = vContext.Types[y];
                            if (subtypeI.Unroll.Final)
                                throw new ValidationException($"SuperType {y} is final and cannot be subtyped");
                            var comptypeI = subtypeI.Expansion;
                            if (!comptype.Matches(comptypeI, vContext.Types))
                                throw new ValidationException($"(sub {defIndex} {comptype}) does not match (sup {y.Value} {comptypeI})");
                        }
                    });

            }
        }
    }
}