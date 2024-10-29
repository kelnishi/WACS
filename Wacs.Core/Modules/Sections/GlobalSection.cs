using System.Collections.Generic;
using System.IO;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{

    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.6 Globals
        /// </summary>
        public List<Global> Globals { get; internal set; } = null!;

        /// <summary>
        /// @Spec 2.5.6. Globals
        /// </summary>
        public class Global
        {
            public readonly Expression Initializer;
            public readonly GlobalType Type;

            public Global(GlobalType type) =>
                (Type, Initializer) = (type, Expression.Empty);

            private Global(BinaryReader reader) =>
                (Type, Initializer) = (GlobalType.Parse(reader), Expression.Parse(reader));

            /// <summary>
            /// @Spec 5.5.9. Global Section
            /// </summary>
            public static Global Parse(BinaryReader reader) => new(reader);

            /// <summary>
            /// @Spec 3.4.4.1 Globals
            /// </summary>
            public class Validator : AbstractValidator<Global>
            {
                public Validator()
                {
                    RuleFor(g => g.Type).SetValidator(new GlobalType.Validator());
                    RuleFor(g => g.Initializer)
                        .Custom((expr, ctx) =>
                        {
                            var validationContext = ctx.GetValidationContext();
                            var subContext = validationContext.PushSubContext(expr);
                            
                            var g = ctx.InstanceToValidate;
                            var exprValidator = new Expression.Validator(g.Type.ResultType, isConstant: true);
                            
                            var result = exprValidator.Validate(subContext);
                            foreach (var error in result.Errors)
                            {
                                ctx.AddFailure($"Expression.{error.PropertyName}", error.ErrorMessage);
                            }
                            
                            validationContext.PopValidationContext();
                        });
                }
            }
        }
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.9 Global Section
        /// </summary>
        private static List<Module.Global> ParseGlobalSection(BinaryReader reader) =>
            reader.ParseList(Module.Global.Parse);
    }
}