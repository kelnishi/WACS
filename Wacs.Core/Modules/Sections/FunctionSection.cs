using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Execution;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        public List<Function> Funcs { get; internal set; } = null!;
        
        /// <summary>
        /// @Spec 2.5.3 Functions
        /// </summary>
        public class Function
        {
            //Function Section only parses the type indices
            public uint TypeIndex { get; internal set; }
            
            //Locals and Body get parsed in the Code Section
            public ValType[] Locals { get; internal set; } = null!;
            public Expression Body { get; internal set; } = null!;

            /// <summary>
            /// @Spec 3.4.1. Functions
            /// </summary>
            public class Validator : AbstractValidator<Function>
            {
                public Validator()
                {
                    // @Spec 3.4.1.1
                    RuleFor(func => func.TypeIndex)
                        .Must((func, index, ctx) =>
                            index < ctx.GetExecContext().Types.Count);
                    RuleFor(func => func)
                        .Custom((func, context) =>
                        {
                            var types = context.GetExecContext().Types;
                            if (!(func.TypeIndex < types.Count))
                            {
                                context.AddFailure("Function.TypeIndex not within Module.Types");
                                return;
                            }

                            var functype = types[(int)func.TypeIndex];

                            context.GetExecContext().SetLocals(functype.ParameterTypes.Types, func.Locals);
                            context.GetExecContext().SetLabels(new[] { functype.ResultType });
                            context.GetExecContext().SetReturn(functype.ResultType);

                            var exprValidator = new Expression.Validator(functype.ResultType);
                            var subcontext = context.GetSubContext(func.Body);
                            var validationResult = exprValidator.Validate(subcontext);
                            if (!validationResult.IsValid)
                            {
                                foreach (var failure in validationResult.Errors)
                                {
                                    // Map the child validation failures to the parent context
                                    // Adjust the property name to reflect the path to the child property
                                    var propertyName = $"{context.PropertyName}.{failure.PropertyName}";
                                    context.AddFailure(propertyName, failure.ErrorMessage);
                                }
                            }
                        });

                }
            }
        }
        
    }
    
    public static partial class ModuleParser
    {
        private static Module.Function ParseIndex(BinaryReader reader) =>
            new Module.Function {
                TypeIndex = reader.ReadLeb128_u32()
            };
        
        /// <summary>
        /// @Spec 5.5.6 Function Section
        /// </summary>
        private static Module.Function[] ParseFunctionSection(BinaryReader reader) =>
            reader.ParseVector(ParseIndex);
    }

}