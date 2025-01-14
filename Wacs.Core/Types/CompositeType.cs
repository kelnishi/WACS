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

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#composite-types⑤
        /// </summary>
        /// <param name="super"></param>
        /// <param name="types"></param>
        /// <returns></returns>
        public bool Matches(CompositeType super, TypesSpace? types) =>
            this switch
            {
                FunctionType ft1 when super is FunctionType ft2 => ft1.Matches(ft2, types),
                StructType st1 when super is StructType st2 => st1.Matches(st2, types),
                ArrayType at1 when super is ArrayType at2 => at1.Matches(at2, types),
                _ => false
            };

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
                                    var propertyName = $"{failure.PropertyName}";
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
                                        var propertyName = $"{failure.PropertyName}";
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
                                    var propertyName = $"{failure.PropertyName}";
                                    ctx.AddFailure(propertyName, failure.ErrorMessage);
                                }
                                break;
                        }
                    });
            }
        }

        public abstract int ComputeHash(int defIndexValue, List<DefType> defs);
    }
}