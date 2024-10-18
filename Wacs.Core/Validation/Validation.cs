using FluentValidation;
using FluentValidation.Results;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module 
    {
        /// <summary>
        /// @Spec 3.4. Modules
        /// </summary>
        public class Validator : AbstractValidator<Module>
        {
            public Validator()
            {
                //Set the validation context
                RuleFor(module => module)
                    .Custom((module, ctx) => {
                        ctx.RootContextData[nameof(WasmValidationContext)] = new WasmValidationContext(module);
                    });
                
                RuleForEach(module => module.Types).SetValidator(new FunctionType.Validator());
                RuleForEach(module => module.Imports).SetValidator(new Import.Validator());
                RuleForEach(module => module.Funcs).SetValidator(new Module.Function.Validator());
                RuleForEach(module => module.Tables).SetValidator(new TableType.Validator());
                RuleForEach(module => module.Memories).SetValidator(new MemoryType.Validator());
                RuleForEach(module => module.Globals).SetValidator(new Global.Validator());
                RuleForEach(module => module.Exports).SetValidator(new Export.Validator());
                RuleForEach(module => module.Elements).SetValidator(new ElementSegment.Validator());
                RuleForEach(module => module.Datas).SetValidator(new Data.Validator());

                RuleFor(module => module.StartIndex)
                    .Must((module, idx, ctx) => ctx.GetValidationContext().Funcs.Contains(idx))
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
                    .When(module => (int)module.StartIndex.Value >= 0);
                
                RuleFor(module => module.StartIndex)
                    .NotEqual(FuncIdx.Default)
                    .WithSeverity(Severity.Warning)
                    .WithMessage($"Module StartIndex was not set");

            }
        }

        public ValidationResult Validate() => new Validator().Validate(this);
        public void ValidateAndThrow() => new Validator().ValidateAndThrow(this);
        
    }
}