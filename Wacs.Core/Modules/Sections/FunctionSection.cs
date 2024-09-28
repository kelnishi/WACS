using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

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
                            index < ((List<FunctionType>)ctx.RootContextData[nameof(Module.Types)]).Count);
                    RuleFor(func => func)
                        .Custom((func, context) =>
                        {
                            var types = context.RootContextData[nameof(Module.Types)] as List<FunctionType>;
                            if (!(func.TypeIndex < types.Count))
                            {
                                context.AddFailure("Function.TypeIndex not within Module.Types");
                                return;
                            }

                            var functype = types[(int)func.TypeIndex];

                            context.RootContextData["locals"] =
                                functype.ParameterTypes.Types.ToList().Concat(func.Locals);
                            context.RootContextData["labels"] = functype.ResultTypes.Types.ToList();
                            context.RootContextData["return"] = functype.ResultTypes.Types.ToList();

                            var exprValidator = new Expression.Validator();
                            var validationResult = exprValidator.Validate(func.Body);
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