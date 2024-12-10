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
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class RecursiveType : IRenderable
    {
        public readonly SubType[] SubTypes;
        //The defType index of the first subtype
        public TypeIdx DefIndex = TypeIdx.Default;

        public RecursiveType(SubType single)
        {
            SubTypes = new[] { single };
        }

        public RecursiveType(SubType[] subTypes)
        {
            SubTypes = subTypes;
        }
        
        public static implicit operator FunctionType(RecursiveType recursiveType)
        {
            var func = recursiveType.SubTypes[0].Body as FunctionType;
            if (func is null)
                throw new InvalidDataException($"RecursiveType ({recursiveType}) was not a FunctionType");
            return func;
        }
        
        /// <summary>
        /// For rendering/debugging
        /// </summary>
        public string Id { get; set; } = "";

        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            var symbol = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
            // var parameters = ParameterTypes.ToParameters();
            // var results = ResultType.ToResults();
            // var func = $" (func{parameters}{results})";
            var func = "";
            
            writer.WriteLine($"{indent}(type{symbol}{func})");
        }

        private static TypeIdx ParseTypeIndexes(BinaryReader reader) => 
            (TypeIdx)reader.ReadLeb128_u32();

        public static RecursiveType Parse(BinaryReader reader) =>
            reader.ReadByte() switch {
                //rec st*
                (byte)RecType.RecSt => new RecursiveType(reader.ParseVector(SubType.Parse)) ,
                //rec st
                //  sub x* ct
                (byte)RecType.SubXCt => new RecursiveType(
                    new SubType(
                        reader.ParseVector(ParseTypeIndexes),
                        CompositeType.ParseTagged(reader),
                        false
                    )
                ),
                //  sub final x* ct
                (byte)RecType.SubFinalXCt => new RecursiveType(
                    new SubType(
                        reader.ParseVector(ParseTypeIndexes),
                        CompositeType.ParseTagged(reader),
                        true
                    )
                ),
                //  sub final E ct
                //    array at
                (byte)CompType.ArrayAt => new RecursiveType(new SubType(ArrayType.Parse(reader), true)),
                //    struct st
                (byte)CompType.StructSt => new RecursiveType(new SubType(StructType.Parse(reader), true)),
                //    func ft
                (byte)CompType.FuncFt => new RecursiveType(new SubType(FunctionType.Parse(reader), true)),
                
                var form => throw new FormatException(
                    $"Invalid type format {form} at offset {reader.BaseStream.Position-1}.")
            };
        
        public class Validator : AbstractValidator<RecursiveType>
        {
            /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-rectypemathsfrechrefsyntax-subtypemathitsubtypeast
            public Validator()
            {
                RuleFor(rt => rt)
                    .Custom((rt, ctx) =>
                    {
                        var vContext = ctx.GetValidationContext();
                        var subTypeValidator = new SubType.Validator(rt);
                        foreach (var (subtype, index) in rt.SubTypes.Select((s,i)=>(s,i)))
                        {
                            var subcontext = vContext.PushSubContext(subtype, index);
                            var validationResult = subTypeValidator.Validate(subcontext);
                            if (!validationResult.IsValid)
                            {
                                foreach (var failure in validationResult.Errors)
                                {
                                    var propertyName = $"{ctx.PropertyPath}.{failure.PropertyName}";
                                    ctx.AddFailure(propertyName, failure.ErrorMessage);
                                }
                            }
                        }
                    });
            }
        }
    }
}