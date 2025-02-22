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
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types.Defs
{
    public class CatchType
    {
        public LabelIdx L;
        public CatchFlags Mode;
        public TagIdx X;

        public CatchType(CatchFlags mode, TagIdx x, LabelIdx l)
        {
            Mode = mode;
            X = x;
            L = l;
        }

        public CatchType(CatchFlags mode, LabelIdx l)
        {
            Mode = mode;
            L = l;
        }

        public static CatchType Parse(BinaryReader reader)
        {
            return (CatchFlags)reader.ReadByte() switch
            {
                CatchFlags.None => new CatchType(CatchFlags.None, (TagIdx)reader.ReadLeb128_u32(), (LabelIdx)reader.ReadLeb128_u32()),
                CatchFlags.CatchRef => new CatchType(CatchFlags.CatchRef, (TagIdx)reader.ReadLeb128_u32(), (LabelIdx)reader.ReadLeb128_u32()),
                CatchFlags.CatchAll => new CatchType(CatchFlags.CatchAll, (LabelIdx)reader.ReadLeb128_u32()),
                CatchFlags.CatchAllRef => new CatchType(CatchFlags.CatchAllRef, (LabelIdx)reader.ReadLeb128_u32()),
                _ => throw new InvalidDataException($"Invalid catch type")                
            };
        }


        public class Validator : AbstractValidator<CatchType>
        {
            public Validator()
            {
                RuleFor(ct => ct.X)
                    .Must((_, index, ctx) => ctx.GetValidationContext().Tags.Contains(index))
                    .When(ct => ct.Mode is CatchFlags.None or CatchFlags.CatchRef)
                    .WithMessage(ct => $"Validation context did not contain Catch Tag Index {ct.X.Value}");
                RuleFor(ct => ct.L)
                    .Must((_, index, ctx) => ctx.GetValidationContext().ContainsLabel(index.Value))
                    .WithMessage(ct => $"Validation context did not contain Catch Label Index {ct.L.Value}");
                
                RuleFor(ct => ct)
                    .Custom((ct, ctx) =>
                    {
                        var tagIdx = ct.X;
                        var labelIdx = ct.L;
                        var vContext = ctx.GetValidationContext();
                        var tag = vContext.Tags[tagIdx];
                        var typeIdx = tag.TypeIndex;
                        if (!vContext.Types.Contains(typeIdx))
                        {
                            ctx.AddFailure($"Validation context did not contain Catch Tag Type Index {typeIdx.Value}");
                            return;
                        }

                        var tagType = vContext.Types[typeIdx];
                        var compType = tagType.Expansion;
                        if (compType is not FunctionType functionType)
                        {
                            ctx.AddFailure($"Catch Tag Type Index {typeIdx.Value} was not a FunctionType");
                            return;
                        }

                        if (functionType.ResultType.Arity != 0)
                        {
                            ctx.AddFailure($"Catch Tag Type Index {typeIdx.Value} had non-empty result type");
                            return;
                        }

                        if (!vContext.ContainsLabel(labelIdx.Value))
                        {
                            ctx.AddFailure($"Validation context did not contain Catch Label Index {ct.L.Value}");
                            return;
                        }

                        var controlFrame = vContext.ControlStack.PeekAt((int)labelIdx.Value);
                        var pType = functionType.ParameterTypes;
                        if (!pType.Matches(controlFrame.EndTypes, vContext.Types))
                        {
                            ctx.AddFailure($"Catch Label {controlFrame.EndTypes.ToNotation()} did not match Catch Tag {functionType.ParameterTypes.ToNotation()}");
                            return;
                        }
                    })
                    .When(ct => ct.Mode is CatchFlags.None);

                RuleFor(ct => ct)
                    .Custom((ct, ctx) =>
                    {
                        var tagIdx = ct.X;
                        var labelIdx = ct.L;
                        var vContext = ctx.GetValidationContext();
                        var tag = vContext.Tags[tagIdx];
                        var typeIdx = tag.TypeIndex;
                        if (!vContext.Types.Contains(typeIdx))
                        {
                            ctx.AddFailure($"Validation context did not contain Catch Tag Type Index {typeIdx.Value}");
                            return;
                        }

                        var tagType = vContext.Types[typeIdx];
                        var compType = tagType.Expansion;
                        if (compType is not FunctionType functionType)
                        {
                            ctx.AddFailure($"Catch Tag Type Index {typeIdx.Value} was not a FunctionType");
                            return;
                        }

                        if (functionType.ResultType.Arity != 0)
                        {
                            ctx.AddFailure($"Catch Tag Type Index {typeIdx.Value} had non-empty result type");
                            return;
                        }

                        if (!vContext.ContainsLabel(labelIdx.Value))
                        {
                            ctx.AddFailure($"Validation context did not contain Catch Label Index {ct.L.Value}");
                            return;
                        }

                        var controlFrame = vContext.ControlStack.PeekAt((int)labelIdx.Value);
                        var pType = functionType.ParameterTypes.Append(ValType.Exn);
                        if (!pType.Matches(controlFrame.EndTypes, vContext.Types))
                        {
                            ctx.AddFailure($"Catch Label {controlFrame.EndTypes.ToNotation()} did not match Catch Tag {functionType.ParameterTypes.ToNotation()}");
                            return;
                        }
                    })
                    .When(ct => ct.Mode is CatchFlags.CatchRef);

                RuleFor(ct => ct)
                    .Custom((ct, ctx) =>
                    {
                        var labelIdx = ct.L;
                        var vContext = ctx.GetValidationContext();
                        if (!vContext.ContainsLabel(labelIdx.Value))
                        {
                            ctx.AddFailure($"Validation context did not contain Catch Label Index {ct.L.Value}");
                            return;
                        }
                        var controlFrame = vContext.ControlStack.PeekAt((int)labelIdx.Value);
                        if (controlFrame.StartTypes.Arity != 0)
                        {
                            ctx.AddFailure($"Catch Label {labelIdx.Value} had non-empty start type");
                            return;
                        }
                    })
                    .When(ct => ct.Mode is CatchFlags.CatchAll);

                RuleFor(ct => ct)
                    .Custom((ct, ctx) =>
                    {
                        var labelIdx = ct.L;
                        var vContext = ctx.GetValidationContext();
                        if (!vContext.ContainsLabel(labelIdx.Value))
                        {
                            ctx.AddFailure($"Validation context did not contain Catch Label Index {ct.L.Value}");
                            return;
                        }
                        var controlFrame = vContext.ControlStack.PeekAt((int)labelIdx.Value);
                        var resultType = new ResultType(ValType.Exn);
                        if (controlFrame.StartTypes.Matches(resultType, vContext.Types))
                        {
                            ctx.AddFailure($"Catch Label {labelIdx.Value} had non-exnref start type");
                            return;
                        }
                    })
                    .When(ct => ct.Mode is CatchFlags.CatchAllRef);

            }
        }
    }
}