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
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public abstract class CompositeType
    {
        public static CompositeType ParseTagged(BinaryReader reader) =>
            reader.ReadByte() switch
            {
                (byte)CompType.ArrayAt => ArrayType.Parse(reader),
                (byte)CompType.StructSt => StructType.Parse(reader),
                (byte)CompType.FuncFt => FunctionType.Parse(reader),
                var form => throw new FormatException(
                    $"Invalid comptype format {form} at offset {reader.BaseStream.Position - 1}.")
            };
        
        public ValType HeapType =>
            this switch
            {
                FunctionType ft => ValType.FuncRef,
                ArrayType at => ValType.Array,
                StructType st => ValType.Struct,
                _ => throw new InvalidDataException($"Unknown CompType:{this}"),
            };

        public bool Matches(CompositeType super)
        {
            throw new NotImplementedException();
        }

        public class Validator : AbstractValidator<CompositeType>
        {
            public Validator()
            {
                RuleFor(ct => ct)
                    .Custom((ct, ctx) =>
                    {
                        var vContext = ctx.GetValidationContext();
                        var fieldValidator = new FieldType.Validator();
                        switch (ct)
                        {
                            case FunctionType ft:
                                var funcValidator = new FunctionType.Validator();
                                var funcContext = vContext.PushSubContext(ft);
                                var funcResult = funcValidator.Validate(funcContext);
                                if (funcResult.IsValid)
                                    break;
                                foreach (var failure in funcResult.Errors)
                                {
                                    var propertyName = $"{ctx.PropertyPath}.{failure.PropertyName}";
                                    ctx.AddFailure(propertyName, failure.ErrorMessage);
                                }
                                break;
                            case StructType st:
                                foreach (var (field,index) in st.FieldTypes.Select((f,i)=>(f,i)))
                                {
                                    var structContext = vContext.PushSubContext(field, index);
                                    var structResult = fieldValidator.Validate(structContext);
                                    if (structResult.IsValid) 
                                        continue;
                                    foreach (var failure in structResult.Errors)
                                    {
                                        var propertyName = $"{ctx.PropertyPath}.{failure.PropertyName}";
                                        ctx.AddFailure(propertyName, failure.ErrorMessage);
                                    }
                                    break;
                                }
                                break;
                            case ArrayType at:
                                var arrayContext = vContext.PushSubContext(at.ElementType);
                                var arrayResult = fieldValidator.Validate(arrayContext);
                                if (arrayResult.IsValid)
                                    break;
                                foreach (var failure in arrayResult.Errors)
                                {
                                    var propertyName = $"{ctx.PropertyPath}.{failure.PropertyName}";
                                    ctx.AddFailure(propertyName, failure.ErrorMessage);
                                }
                                break;
                        }
                    });
            }
        }
    }
}