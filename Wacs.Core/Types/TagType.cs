// Copyright 2025 Kelvin Nishikawa
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

using System.IO;
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class TagType
    {
        public TagTypeAttribute Attribute;
        public TypeIdx TypeIndex;

        public TagType(TagTypeAttribute attribute, TypeIdx typeIndex)
        {
            Attribute = attribute;
            TypeIndex = typeIndex;
        }

        public static TagType Parse(BinaryReader reader)
        {
            return new TagType(
                (TagTypeAttribute)reader.ReadByte(),
                (TypeIdx)reader.ReadLeb128_u32()
            );
        }

        public class Validator : AbstractValidator<TagType>
        {
            public Validator()
            {
                RuleFor(tt => tt.Attribute)
                    .IsInEnum();
                RuleFor(tt => tt.TypeIndex)
                    .Must((_, index, ctx) => ctx.GetValidationContext().Types.Contains(index));
                RuleFor(tt => tt)
                    .Custom((tt, ctx) =>
                    {
                        var vContext = ctx.GetValidationContext();
                        if (!vContext.Types.Contains(tt.TypeIndex))
                            ctx.AddFailure($"Type index {tt.TypeIndex} not found in types");
                        
                        var tagType = vContext.Types[tt.TypeIndex];
                        var compType = tagType.Expansion;
                        if (compType is not FunctionType functionType)
                        {
                            ctx.AddFailure($"Tag type must be a function type, found {compType}");
                            return;
                        }
                        if (functionType.ResultType.Arity != 0)
                            ctx.AddFailure($"Tag type result must be empty. Type Result was {functionType.ResultType}");
                    });
            }
        }
    }
}