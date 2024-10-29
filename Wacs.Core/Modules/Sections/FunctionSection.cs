using System.Collections.Generic;
using System.IO;
using FluentValidation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
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
            public bool IsImport = false;

            //Function Section only parses the type indices
            public TypeIdx TypeIndex { get; internal set; }

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
                        .Must((_, index, ctx) =>
                            ctx.GetValidationContext().Types.Contains(index));
                    RuleFor(func => func)
                        .Custom((func, ctx) =>
                        {
                            var vContext = ctx.GetValidationContext();
                            var types = vContext.Types;
                            if (!types.Contains(func.TypeIndex))
                            {
                                ctx.AddFailure($"Function validation failure at {ctx.PropertyPath}: Function.TypeIndex not within Module.Types");
                            }

                            var funcType = types[func.TypeIndex];
                            vContext.PushFrame(func);
                            var retType = vContext.Return;
                            var label = new Label(retType, InstructionPointer.Nil, OpCode.Nop);
                            vContext.Frame.Labels.Push(label);

                            var exprValidator = new Expression.Validator(funcType.ResultType);
                            var subcontext = ctx.GetSubContext(func.Body);
                            var validationResult = exprValidator.Validate(subcontext);
                            if (!validationResult.IsValid)
                            {
                                foreach (var failure in validationResult.Errors)
                                {
                                    // Map the child validation failures to the parent context
                                    // Adjust the property name to reflect the path to the child property
                                    var propertyName = $"{ctx.PropertyPath}.{failure.PropertyName}";
                                    ctx.AddFailure(propertyName, failure.ErrorMessage);
                                }
                            }

                            vContext.PopFrame();
                            try
                            {
                                vContext.OpStack.ValidateStack(retType);
                            }
                            catch (ValidationException exc)
                            {
                                ctx.AddFailure($"Function validation failure at {ctx.PropertyPath}: {exc.Message}");
                            }
                        });
                }
            }
        }
    }

    public static partial class BinaryModuleParser
    {
        private static Module.Function ParseIndex(BinaryReader reader) =>
            new()
            {
                TypeIndex = (TypeIdx)reader.ReadLeb128_u32()
            };

        /// <summary>
        /// @Spec 5.5.6 Function Section
        /// </summary>
        private static Module.Function[] ParseFunctionSection(BinaryReader reader) =>
            reader.ParseVector(ParseIndex);
    }
}