using System;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using Wacs.Core.Execution;

namespace Wacs.Core.Validation
{
    public static class ValidationUtility
    {
        public static Execution.ExecContext GetExecContext<T>(this ValidationContext<T> context)
            where T : class
            => context.RootContextData.TryGetValue(nameof(ExecContext), out var execContextData) && execContextData is ExecContext execContext
                ? execContext
                : throw new InvalidOperationException($"The ExecContext is not present in the RootContextData.");

        public static ValidationContext<T> GetSubContext<TP, T>(this ValidationContext<TP> parent, T child)
            where T : class
            where TP : class
            => new ValidationContext<T>(child)
            {
                RootContextData =
                {
                    [nameof(ExecContext)] = parent.GetExecContext()
                }
            };
        
        
        public static ValidationResult ValidateModule(Module module)
        {
            var context = new ValidationContext<Module>(module) {
                RootContextData = {
                    [nameof(ExecContext)] = ExecContext.CreateValidationContext(module)
                }
            };
            
            var validator = new Module.Validator();
            return validator.Validate(context);
        }
    }

}