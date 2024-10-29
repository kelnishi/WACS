using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using Wacs.Core.Instructions;
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

        public List<Function> ValidationFuncs => ImportedFunctions.Concat(Funcs).ToList();

        //Function[100].Expression[34].NumericInst
        public int CalculateLine(string validationPath, bool print, out string instruction)
        {
            int line = 1;
            line += Types.Count;
            line += Imports.Length;
            //Start of functions
            var parts = validationPath.Split(".");
            instruction = "";

            IBlockInstruction pointerInst = null;
            InstructionSequence seq = null;
            string indent = "";
            foreach (var part in parts)
            {
                indent += " ";
                //Skip instruction strata
                if (!part.EndsWith("]"))
                {
                    line += 1;
                    break;
                }
                
                var regex = new Regex(@"(\w+)\[(\d+)\]");
                var match = regex.Match(part);
                if (match.Success)
                {
                    int index = int.Parse(match.Groups[2].Value);
                    string strata = match.Groups[1].Value;
                    switch (strata)
                    {
                        case "Function":
                        {
                            index -= Imports.Length;
                            for (int i = 0; i < index; ++i)
                            {
                                line += Funcs[i].Size;
                            }

                            line += 1;
                        
                            if (print)
                                Console.WriteLine($"{indent}Function[{index}]:{line}");
                        
                            if (Funcs[index].Locals.Length > 0)
                                line += 1;

                            instruction = "func";

                            seq = Funcs[index].Body.Instructions;
                            break;
                        }
                        case "Expression":
                        case "Block":
                        {
                            if (seq == null)
                                throw new ArgumentException("Validation path was invalid.");
                        
                            //Fast-forward through instructions
                            for (int i = 0; i < index; ++i)
                            {
                                var inst = seq[i];
                                if (inst is IBlockInstruction blockInstruction)
                                {
                                    line += blockInstruction.Size;
                                }
                                else
                                {
                                    line += 1;
                                }
                            }
                            if (print)
                                Console.WriteLine($"{indent}{strata}[{index}]:{line}");
                        
                            var term = seq[index];
                            if (term is IBlockInstruction blTerm)
                            {
                                pointerInst = blTerm;
                            }
                            instruction = term.Op.GetMnemonic();
                            break;
                        }
                        case "InstBlock":
                        case "InstIf":
                        case "InstLoop":
                        {
                            if (pointerInst == null)
                                throw new ArgumentException("Validation path was invalid.");
                        
                            if (print)
                                Console.WriteLine($"{indent}{strata}[{index}]:{line}");
                        
                            seq = pointerInst.GetBlock(index);
                            break;
                        }
                    }
                }
            }

            return line;
        }


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

            public int Size => (Locals.Length > 0?1:0) + Body.Size;

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
                            if (func.IsImport)
                                return;
                            
                            var vContext = ctx.GetValidationContext();
                            var types = vContext.Types;
                            if (!types.Contains(func.TypeIndex))
                            {
                                ctx.AddFailure($"{ctx.PropertyPath}: Function.TypeIndex not within Module.Types");
                            }

                            var funcType = types[func.TypeIndex];
                            vContext.PushFrame(func);
                            var retType = vContext.Return;
                            var label = new Label(retType, InstructionPointer.Nil, OpCode.Nop);
                            vContext.Frame.Labels.Push(label);

                            var exprValidator = new Expression.Validator(funcType.ResultType);
                            var subcontext = vContext.PushSubContext(func.Body);
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
                            vContext.PopValidationContext();

                            vContext.PopFrame();
                            try
                            {
                                vContext.OpStack.ValidateStack(retType);
                            }
                            catch (ValidationException exc)
                            {
                                ctx.AddFailure($"{ctx.PropertyPath}: {exc.Message}");
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