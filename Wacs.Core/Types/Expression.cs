// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.4.9. Expressions
    /// </summary>
    public class Expression : ILabelTarget
    {
        public static readonly Expression Empty = new(FunctionType.Empty, InstructionSequence.Empty, true);

        private Expression(InstructionSequence seq, bool isStatic)
        {
            Instructions = seq;
            IsStatic = isStatic;
            Label = new Label
            {
                ContinuationAddress = InstructionPointer.Nil,
                Instruction = OpCode.Nop,
                StackHeight = -1,
            };
            EnclosingBlock = this;
            LinkLabelTarget(Instructions, this);
        }
        
        public Expression(FunctionType type, InstructionSequence seq, bool isStatic)
        {
            Instructions = seq;
            IsStatic = isStatic;
            Label = new Label
            {
                ContinuationAddress = InstructionPointer.Nil,
                Instruction = OpCode.Nop,
                Arity = type.ResultType.Arity,
                StackHeight = -1,
            };
            EnclosingBlock = this;
            LinkLabelTarget(Instructions, this);
        }

        public Expression(IInstruction single)
        {
            IsStatic = true;
            Instructions = new InstructionSequence(new List<IInstruction> { single });
            Label = new Label
            {
                ContinuationAddress = InstructionPointer.Nil,
                Instruction = OpCode.Nop,
                StackHeight = -1,
            };
            EnclosingBlock = this;
            LinkLabelTarget(Instructions, this);
        }

        //TODO: compute stack heights
        private void LinkLabelTarget(InstructionSequence seq, ILabelTarget enclosingTarget)
        {
            for (int i = 0; i < seq.Count; ++i)
            {
                var inst = seq._instructions[i];
                if (inst is IBlockInstruction target)
                {
                    target.EnclosingBlock = enclosingTarget;
                    var label = new Label
                    {
                        ContinuationAddress = new InstructionPointer(seq, i),
                        Instruction = inst.Op,
                        StackHeight = -1,
                    };
                    switch (inst)
                    {
                        case InstBlock b: 
                            b.Label = label;
                            LinkLabelTarget(b.GetBlock(0), b);
                            break;
                        case InstLoop b: 
                            b.Label = label;
                            LinkLabelTarget(b.GetBlock(0), b);
                            break;
                        case InstIf b: 
                            b.Label = label;
                            LinkLabelTarget(b.GetBlock(0), b);
                            LinkLabelTarget(b.GetBlock(1), b);
                            break;
                    }
                }
            }
        }

        public ILabelTarget EnclosingBlock { get; set; }
        public Label Label;

        public readonly bool IsStatic;
        public readonly InstructionSequence Instructions;

        public int Size => Instructions.Size;

        /// <summary>
        /// Leaves the result on the OpStack
        /// </summary>
        /// <param name="context"></param>
        public void Execute(ExecContext context)
        {
            var frame = context.ReserveFrame(context.Frame.Module, FunctionType.Empty, FuncIdx.ExpressionEvaluation);
            
            // var label = new Label{StackHeight = -1}; //frame.Labels.Reserve();
            // label.Set(ResultType.Empty, new InstructionPointer(Instructions, 1), OpCode.Nop, context.OpStack.Count);
            // frame.Labels.Push(label);
            
            Label.Set(ResultType.Empty, new InstructionPointer(Instructions, 1), OpCode.Nop, context.OpStack.Count - frame.StackHeight);
            frame.PushLabel(this);
            
            
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
            new(new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, IInstruction.IsEnd)), true);

        /// <summary>
        /// For Single instruction renders (globals, elements)
        /// </summary>
        /// <returns></returns>
        public string ToWat()
        {
            var inst = Instructions[0];
            var instText = $" ({inst?.RenderText(null)??"null"})";
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
        }
    }
}