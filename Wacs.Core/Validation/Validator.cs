using System.Linq;
using FluentValidation;

namespace Wacs.Core.Validation
{
    public static class ValidationUtility
    {
        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// </summary>
        public static ValidationContext<Module> CreateValidationContext(Module module) =>
            new ValidationContext<Module>(module) {
                RootContextData = {
                    [nameof(Module.Types)] = module.Types.ToList(),
                    [nameof(Module.Funcs)] = module.Funcs,
                    [nameof(Module.Tables)] = module.Tables.ToList(),
                    [nameof(Module.Mems)] = module.Mems,
                    [nameof(Module.Globals)] = module.Globals.Select(g => g.Type).ToList(),
                    [nameof(Module.Elements)] = module.Elements.ToList(),
                    [nameof(Module.Datas)] = module.Datas.ToList(),
                    
                    //TODO: Locals (current function)
                    //TODO: Labels (stack of accessible labels)
                    //TODO: Return type
                    //TODO: References (function indices)
                }
            };

        public static void ValidateModule(Module module)
        {
            var context = CreateValidationContext(module);
            var validator = new Module.Validator();
            validator.Validate(context);
        }
    }

}