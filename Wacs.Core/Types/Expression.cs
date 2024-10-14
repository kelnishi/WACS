using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Execution;
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
        //Instructions terminate with an explicit END instruction. (omitted here)
        // !!!Need to output the END instruction when serializing!!!
        public List<IInstruction> Instructions { get; internal set; }

        private Expression() =>
            Instructions = new List<IInstruction>();
        
        public Expression(IInstruction[] instruction) =>
            Instructions = instruction.ToList();

        public Expression(IInstruction single) => 
            Instructions = new List<IInstruction> { single };

        private Expression(List<IInstruction> instructions) =>
            Instructions = instructions;
            
        public static Expression Empty => new Expression();

        public bool IsConstant() =>
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
        
        /// <summary>
        /// @Spec 5.4.9 Expressions
        /// </summary>
        public static Expression Parse(BinaryReader reader) =>
            new Expression(reader.ParseUntilNull(InstructionParser.Parse));

        /// <summary>
        /// @Spec 3.3.10. Expressions
        /// </summary>
        public class Validator : AbstractValidator<Expression>
        {
            public ResultType ResultType { get; }
            private ValType StackType { get; set; }

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
                            inst.Execute(ctx.GetExecContext());
                            StackValue resultVal = ctx.GetExecContext().Stack.Peek();
                            StackType = resultVal.Type;
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
                            Console.WriteLine(exc);
                            ctx.AddFailure($"WASM Instruction `{inst.OpCode.GetMnemonic()}` is not implemented.");
                        }
                    });
                RuleFor(e => e).Custom((e, ctx) =>
                {
                    if (ShouldBeConstant)
                        if(!e.IsConstant())
                            ctx.AddFailure($"Expression must be constant");
                    
                    var execContext = ctx.GetExecContext();
                    foreach (var eType in ResultType.Types)
                    {
                        var rValue = execContext.Stack.PopAny();
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