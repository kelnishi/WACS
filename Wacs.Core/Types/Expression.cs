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

using System;
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
        public static readonly Expression Empty = new(0, InstructionSequence.Empty, true);
        public readonly InstructionSequence Instructions;

        public readonly bool IsStatic;

        public InstExpressionProxy LabelTarget;

        /// <summary>
        /// For parsing normal Code sections
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="isStatic"></param>
        private Expression(InstructionSequence seq, bool isStatic)
        {
            Instructions = seq;
            IsStatic = isStatic;
            LabelTarget = new(new Label
            {
                //Compute Arity in PrecomputeLabels
                ContinuationAddress = new InstructionPointer(Instructions, 1),
                Instruction = OpCode.Expr,
                StackHeight = -1,
            });
        }

        /// <summary>
        /// Manual construction (from optimizers or statics)
        /// </summary>
        /// <param name="arity"></param>
        /// <param name="seq"></param>
        /// <param name="isStatic"></param>
        public Expression(int arity, InstructionSequence seq, bool isStatic)
        {
            Instructions = seq;
            IsStatic = isStatic;
            LabelTarget = new(new Label
            {
                Arity = arity,
                ContinuationAddress = new InstructionPointer(Instructions, 1),
                Instruction = OpCode.Expr,
                StackHeight = 0,
            });
        }

        //Single Initializer
        public Expression(InstructionBase single, int arity)
        {
            IsStatic = true;
            Instructions = new InstructionSequence(new List<InstructionBase> { single });
            LabelTarget = new (new Label
            {
                Arity = arity,
                ContinuationAddress = new InstructionPointer(Instructions, 1),
                Instruction = OpCode.Expr,
                StackHeight = 0,
            });
        }

        public int Size => Instructions.Size;

        public void PrecomputeLabels(IWasmValidationContext vContext)
        {
            LinkLabelTarget(vContext, Instructions, LabelTarget);
        }

        private void LinkLabelTarget(IWasmValidationContext vContext, InstructionSequence seq, BlockTarget enclosingTarget)
        {
            for (int i = 0; i < seq.Count; ++i)
            {
                var inst = seq._instructions[i];
                inst.Validate(vContext);
                
                if (inst is BlockTarget target)
                {
                    target.EnclosingBlock = enclosingTarget;
                    var blockInst = target as IBlockInstruction;

                    var block = blockInst!.GetBlock(0);
                    int arity = 0;
                    try
                    {
                        var funcType = vContext.Types.ResolveBlockType(block.BlockType);
                        if (funcType == null)
                            throw new IndexOutOfRangeException();

                        arity = inst is InstLoop ? funcType.ParameterTypes.Arity : funcType.ResultType.Arity;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        throw new InvalidDataException($"Failure computing Labels. BlockType:{block.BlockType} did not exist in the Module");
                    }

                    var label = target.Label ?? new Label();
                    label.Arity = arity;
                    label.ContinuationAddress = new InstructionPointer(seq, i);
                    label.Instruction = inst.Op;
                    //HACK: Use any existing precomputed StackHeight (assume optimization has not changed this value)
                    label.StackHeight = (target.Label?.StackHeight ?? -1) >= 0
                        ? target.Label!.StackHeight
                        : vContext.OpStack.Height;
                    
                    for (int b = 0; b < blockInst!.Count; ++b)
                        LinkLabelTarget(vContext,blockInst.GetBlock(b).Instructions, target);
                    
                    target.Label = label;
                }
            }
        }

        /// <summary>
        /// Leaves the result on the OpStack
        /// </summary>
        /// <param name="context"></param>
        public void ExecuteInitializer(ExecContext context)
        {
            var frame = context.ReserveFrame(context.Frame.Module, FunctionType.Empty, FuncIdx.ExpressionEvaluation);
            if (context.OpStack.Count != 0)
                throw new InvalidDataException("OpStack should be empty");
            frame.ReturnLabel = LabelTarget.Label;
            frame.SetLabel(LabelTarget);
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
            new(new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, InstructionBase.IsEnd)), true);

        public static Expression ParseInitializer(BinaryReader reader) =>
            new(1, new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, InstructionBase.IsEnd)), true);

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