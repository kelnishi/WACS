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

using FluentValidation;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    /// <summary>
    /// @Spec 3.4. Modules
    /// </summary>
    public class ModuleValidator : AbstractValidator<Module>
    {
        public ModuleValidator()
        {
            //Set the validation context
            RuleFor(module => module)
                .Custom((module, ctx) =>
                {
                    ctx.RootContextData[nameof(WasmValidationContext)] = new WasmValidationContext(module, ctx);
                });

            RuleForEach(module => module.Types).SetValidator(new RecursiveType.Validator());
            RuleForEach(module => module.Imports).SetValidator(new Module.Import.Validator());
            RuleFor(module => module.Imports)
                .Custom((_, ctx) => 
                    ctx.GetValidationContext().Globals.SetHighImportWatermark());
            RuleForEach(module => module.Tables).SetValidator(new TableType.Validator());
            RuleForEach(module => module.Memories).SetValidator(new MemoryType.Validator());
            RuleForEach(module => module.Globals).SetValidator(new Module.Global.Validator());
            RuleForEach(module => module.ValidationFuncs)
                .SetValidator(new Module.Function.Validator()).OverridePropertyName("Function");
            RuleForEach(module => module.Exports).SetValidator(new Module.Export.Validator());
            RuleForEach(module => module.Elements).SetValidator(new Module.ElementSegment.Validator());
            RuleForEach(module => module.Datas).SetValidator(new Module.Data.Validator());

            RuleFor(module => module.StartIndex)
                .Must((_, idx, ctx) => ctx.GetValidationContext().Funcs.Contains(idx))
                .Custom((idx, ctx) =>
                {
                    var execContext = ctx.GetValidationContext();
                    var typeIndex = execContext.Funcs[idx].TypeIndex;
                    var type = execContext.Types[typeIndex].Expansion;
                    //TODO: handle any type

                    if (type is FunctionType funcType)
                    {
                        if (funcType.ParameterTypes.Arity != 0 || funcType.ResultType.Arity != 0)
                        {
                            ctx.AddFailure($"Invalid Start function with type: {type}");
                        }
                    }
                })
                .When(module => module.StartIndex.Value < module.Funcs.Count);
        }
    }
}