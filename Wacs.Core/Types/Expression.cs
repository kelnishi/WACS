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
        public List<IInstruction> Instructions { get; internal set; }

        private Expression() =>
            Instructions = new List<IInstruction>();
        
        public Expression(IInstruction[] instruction) =>
            Instructions = instruction.ToList();

        public Expression(IInstruction single) => 
            Instructions = new List<IInstruction> { single };

        private Expression(List<IInstruction> instructions) =>
            Instructions = instructions;
            
        public static readonly Expression Empty = new();

        public bool IsConstant() =>
            Instructions.Count == 1 &&
            !Instructions.Any(inst => inst.OpCode switch {
                OpCode.I32Const => false,
                OpCode.I64Const => false,
                OpCode.F32Const => false,
                OpCode.F64Const => false,
                OpCode.GlobalGet => false,
                OpCode.RefNull => false,
                OpCode.RefFunc => false,
                _ => true
            });

        public void Execute(ExecContext context)
        {
            foreach (var inst in Instructions)
            {
                inst.Execute(context);
            }
        }
        
        /// <summary>
        /// @Spec 5.4.9 Expressions
        /// </summary>
        public static Expression Parse(BinaryReader reader) =>
            new(reader.ParseUntil(InstructionParser.Parse, InstructionParser.IsEnd));

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
                // @Spec 3.3.9. Instruction Sequences
                RuleForEach(e => e.Instructions)
                    .Custom((inst, ctx) =>
                    {
                        try
                        {
                            inst.Validate(ctx.GetValidationContext());
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
                    });
                RuleFor(e => e).Custom((e, ctx) =>
                {
                    if (ShouldBeConstant)
                        if(!e.IsConstant())
                            ctx.AddFailure($"Expression must be constant");
                    
                    var validationContext = ctx.GetValidationContext();
                    
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