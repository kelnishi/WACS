using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.4.9. Expressions
    /// </summary>
    public class Expression
    {
        public static readonly Expression Empty = new(InstructionSequence.Empty);

        private Expression(InstructionSequence seq) =>
            Instructions = seq;

        public Expression(IInstruction single) =>
            Instructions = new InstructionSequence(single);

        public InstructionSequence Instructions { get; }

        public int Size => Instructions.Size;

        /// <summary>
        /// Leaves the result on the OpStack
        /// </summary>
        /// <param name="context"></param>
        public void Execute(ExecContext context)
        {
            var frame = new Frame(context.Frame.Module, FunctionType.Empty);
            frame.Index = FuncIdx.ExpressionEvaluation;
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
            new(new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, IInstruction.IsEnd)));

        /// <summary>
        /// For Single instruction renders (globals, elements)
        /// </summary>
        /// <returns></returns>
        public string ToWat()
        {
            var inst = this.Instructions[0];
            var instText = $" ({inst.RenderText(null)})";
            return instText;
        }

        public bool ContainsInstructions(HashSet<ByteCode> opcodes) => 
            Instructions.ContainsInstruction(opcodes);

        /// <summary>
        /// @Spec 3.3.10. Expressions
        /// </summary>
        public class Validator : AbstractValidator<Expression>
        {
            public Validator(ResultType resultType, bool isConstant = false)
            {
                ResultType = resultType;

                RuleFor(e => e).Custom((e, ctx) =>
                {
                    var validationContext = ctx.GetValidationContext();
                    
                    if (isConstant)
                        if (!e.Instructions.IsConstant(validationContext))
                            ctx.AddFailure($"Expression must be constant");


                    var funcType = new FunctionType(ResultType.Empty, resultType);
                    validationContext.PushControlFrame(OpCode.Expr, funcType); //The root frame
                    validationContext.PushControlFrame(OpCode.Block, funcType); //For the end instruction
                    
                    var instructionValidator = new WasmValidationContext.InstructionValidator();

                    int lastIndex = e.Instructions.Count - 1;
                    // @Spec 3.3.9. Instruction Sequences
                    foreach (var (inst, index) in e.Instructions.Select((inst, index)=>(inst, index)))
                    {
                        //Skip End for constant expressions
                        if (isConstant && index == lastIndex && inst.Op == OpCode.End)
                            break;
                        
                        var subContext = validationContext.PushSubContext(inst, index);

                        try
                        {
                            var result = instructionValidator.Validate(subContext);
                            foreach (var error in result.Errors)
                            {
                                ctx.AddFailure($"Instruction.{error.PropertyName}", error.ErrorMessage);
                            }
                        }
                        catch (ValidationException exc)
                        {
                            ctx.AddFailure(exc.Message);
                        }
                        
                        validationContext.PopValidationContext();
                    }

                    try
                    {
                        validationContext.PopControlFrame();
                        if (validationContext.OpStack.Height != 0)
                            throw new ValidationException($"Expression had leftover operands on the stack");
                        
                    }
                    catch (ValidationException exc)
                    {
                        ctx.AddFailure($"{ctx.PropertyPath}: {exc.Message}");
                    }
                });
            }

            private ResultType ResultType { get; }
        }
    }
}