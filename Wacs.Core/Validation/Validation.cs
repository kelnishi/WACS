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

            RuleForEach(module => module.Types).SetValidator(new FunctionType.Validator());
            RuleForEach(module => module.Imports).SetValidator(new Module.Import.Validator());
            RuleForEach(module => module.ValidationFuncs)
                .SetValidator(new Module.Function.Validator()).OverridePropertyName("Function");
            RuleForEach(module => module.Tables).SetValidator(new TableType.Validator());
            RuleForEach(module => module.Memories).SetValidator(new MemoryType.Validator());
            RuleFor(module => module.MemoryCount)
                .LessThan(2)
                .WithMessage("Multiple memories are not supported.");
            RuleForEach(module => module.Globals).SetValidator(new Module.Global.Validator());
            RuleForEach(module => module.Exports).SetValidator(new Module.Export.Validator());
            RuleForEach(module => module.Elements).SetValidator(new Module.ElementSegment.Validator());
            RuleForEach(module => module.Datas).SetValidator(new Module.Data.Validator());

            RuleFor(module => module.StartIndex)
                .Must((_, idx, ctx) => ctx.GetValidationContext().Funcs.Contains(idx))
                .Custom((idx, ctx) =>
                {
                    var execContext = ctx.GetValidationContext();
                    var typeIndex = execContext.Funcs[idx].TypeIndex;
                    var type = execContext.Types[typeIndex];
                    if (type.ParameterTypes.Length != 0 || type.ResultType.Length != 0)
                    {
                        ctx.AddFailure($"Invalid Start function with type: {type}");
                    }
                })
                .When(module => module.StartIndex.Value < module.Funcs.Count);
        }
    }
}