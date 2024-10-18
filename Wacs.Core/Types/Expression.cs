using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Runtime;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.4.9. Expressions
    /// </summary>
    public class Expression
    {
        public InstructionSequence Instructions { get; }

        private Expression(InstructionSequence seq) =>
            Instructions = seq;
        
        public Expression(IInstruction single) =>
            Instructions = new(single);
        
        public static readonly Expression Empty = new(InstructionSequence.Empty);

        public bool IsConstant => Instructions.IsConstant;
        
        /// <summary>
        /// Leaves the result on the OpStack
        /// </summary>
        /// <param name="context"></param>
        public void Execute(ExecContext context)
        {
            var frame = new Frame(context.Frame.Module, FunctionType.Empty);
            var label = new Label(ResultType.Empty, new InstructionPointer(Instructions, 1), OpCode.Nop);
            frame.Labels.Push(label);
            context.PushFrame(frame);
            foreach (var inst in Instructions)
            {
                inst.Execute(context);
            }
            context.PopFrame();
        }
        
        /// <summary>
        /// @Spec 5.4.9 Expressions
        /// </summary>
        public static Expression Parse(BinaryReader reader) =>
            new(new InstructionSequence(reader.ParseUntil(InstructionParser.Parse, InstructionParser.IsEnd)));

        /// <summary>
        /// @Spec 3.3.10. Expressions
        /// </summary>
        public class Validator : AbstractValidator<Expression>
        {
            public ResultType ResultType { get; }

            public bool ShouldBeConstant;
            
            public Validator(ResultType resultType, bool isConstant = false)
            {
                ResultType = resultType;
                ShouldBeConstant = isConstant;

                RuleFor(e => e).Custom((e, ctx) =>
                {
                    if (ShouldBeConstant)
                        if(!e.IsConstant)
                            ctx.AddFailure($"Expression must be constant");
                    
                    var validationContext = ctx.GetValidationContext();

                    // @Spec 3.3.9. Instruction Sequences
                    foreach (var inst in e.Instructions)
                    {
                        try
                        {
                            inst.Validate(validationContext);
                        }
                        catch (InvalidProgramException exc)
                        {
                            ctx.AddFailure(exc.Message);
                        }
                        catch (InvalidDataException exc)
                        {
                            ctx.AddFailure(exc.Message);
                        }
                        catch (NotImplementedException exc)
                        {
                            _ = exc;
                            ctx.AddFailure($"WASM Instruction `{inst.OpCode.GetMnemonic()}` is not implemented.");
                        }
                    }
                    
                    foreach (var eType in ResultType.Types)
                    {
                        var rValue = validationContext.OpStack.PopAny();
                        if (rValue.Type != eType)
                        {
                            ctx.AddFailure($"Expression Result type {rValue.Type} did not match expected {eType}");
                        }
                    }
                });
            }
        }
    }
}