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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    /// <summary>
    /// @Spec 3.1.1. Contexts
    /// </summary>
    public class WasmValidationContext : IWasmValidationContext
    {
        private static Stack<Value> _aside = new();
        private readonly Stack<IValidationContext> _contextStack = new();

        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// @Spec 3.4.10. Modules
        /// </summary>
        public WasmValidationContext(Module module, ValidationContext<Module> rootContext)
        {
            ValidationModule = new ModuleInstance(module);

            Stack = new(this);
            
            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = new ElementsSpace(module.Elements.ToList());
            Datas = new DataValidationSpace(module.Datas.Length);

            Globals = new GlobalValidationSpace(module);

            RootContext = rootContext;
            _contextStack.Push(rootContext);
        }

        private Frame ExecFrame { get; set; } = null!;
        private ValidationOpStack Stack { get; }

        public ValidationContext<Module> RootContext { get; }

        public ResultType Return => ControlFrame.EndTypes;
        private ModuleInstance ValidationModule { get; }
        public RuntimeAttributes Attributes { get; set; } = new();

        public FuncIdx FunctionIndex { get; set; } = FuncIdx.Default;
        public IValidationOpStack OpStack => Stack;

        public ResultType ReturnType => ExecFrame.Type.ResultType;


        public Stack<ValidationControlFrame> ControlStack { get; } = new();
        public ValidationControlFrame ControlFrame => ControlStack.Peek();
        public TypesSpace Types => ValidationModule.Types;
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals => ExecFrame.Locals;
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }

        public bool Unreachable { get; set; }

        /// <summary>
        /// @Spec A.3 Validation Algorithm
        /// </summary>
        public void SetUnreachable()
        {
            // Unreachable = true;
            PopOperandsToHeight(ControlFrame.Height);
            ControlFrame.Unreachable = true;
        }

        public void Assert(bool factIsTrue, string formatString, params object[] args)
        {
            if (factIsTrue) return;
            throw new ValidationException(string.Format(formatString, args));
        }

        public void Assert([NotNull] object? objIsNotNull, string formatString, params object[] args)
        {
            if (objIsNotNull != null) return;
            throw new ValidationException(string.Format(formatString, args));
        }

        public void ValidateBlock(Block instructionBlock, int index = 0)
        {
            var blockContext = PushSubContext(instructionBlock, index);
            
            var blockValidator = new Block.Validator();
            var blockResult = blockValidator.Validate(blockContext);
            foreach (var error in blockResult.Errors)
            {
                RootContext.AddFailure($"Block.{error.PropertyName}", error.ErrorMessage);
            }

            var instructionValidator = new InstructionValidator();
            int instIdx = 0;
            foreach (var inst in instructionBlock.Instructions)
            {
                var subContext = PushSubContext(inst, instIdx++);
                
                var result = instructionValidator.Validate(subContext);
                foreach (var error in result.Errors)
                {
                    RootContext.AddFailure($"Block Instruction.{error.PropertyName}", error.ErrorMessage);
                }
                
                PopValidationContext();
            }
            
            PopValidationContext();
        }

        public void PushControlFrame(ByteCode opCode, FunctionType types)
        {
            var frame = new ValidationControlFrame
            {
                Opcode = opCode,
                Types = types,
                Height = OpStack.Height,
            };
            
            ControlStack.Push(frame);
            
            OpStack.PushResult(types.ParameterTypes);
        }

        public ValidationControlFrame PopControlFrame()
        {
            if (ControlStack.Count == 0)
                throw new ValidationException("Validation Control Stack underflow");
            
            //Check to make sure we have the correct results, but only if we didn't jump
            OpStack.PopValues(ControlFrame.EndTypes, ref _aside);
            _aside.Clear();
            
            //Check the stack
            if (OpStack.Height != ControlFrame.Height)
                throw new ValidationException(
                    $"Operand stack height {OpStack.Height} differed from Control Frame height {ControlFrame.Height}");
            
            // if (ControlFrame.ConditionallyReachable)
            //     Unreachable = false;

            return ControlStack.Pop();
        }

        public bool ContainsLabel(uint label) => ControlStack.Count - 2 >= label;

        public void PopOperandsToHeight(int height)
        {
            if (OpStack.Height < height)
                throw new InvalidDataException("Operand Stack underflow.");
            
            while (OpStack.Height > height)
            {
                OpStack.PopAny();
            }
        }

        public ValidationContext<T> PushSubContext<T>(T child, int index = -1)
            where T : class
        {
            var subctx = _contextStack.Peek().GetSubContext(child, index);
            _contextStack.Push(subctx);
            return subctx;
        }

        public void PopValidationContext() => _contextStack.Pop();

        public void SetExecFrame(FunctionType funcType, ValType[] localTypes)
        {
            int capacity = funcType.ParameterTypes.Types.Length + localTypes.Length;
            var data = new Value[capacity];
            var locals = new LocalsSpace(data, funcType.ParameterTypes.Types, localTypes);
            ExecFrame = new Frame
            {
                Module = ValidationModule,
                Type = funcType,
                Locals = locals
            };
        }

        public class InstructionValidator : AbstractValidator<IInstruction>
        {
            public InstructionValidator()
            {
                RuleFor(inst => inst)
                    .Custom((inst, ctx) =>
                    {
                        try
                        {
                            inst.Validate(ctx.GetValidationContext());
                        }
                        catch (ValidationException exc)
                        {
                            string message = $"{exc.Message}";
                            if (!exc.Message.StartsWith("Function["))
                            {
                                string path = ctx.PropertyPath;
                                var (line, _) = ctx.GetValidationContext().ValidationModule.Repr.CalculateLine(path);
                                message = $"{ctx.PropertyPath} line {line}: {message} in Instruction {inst.Op.GetMnemonic()}";
                            }
                            ctx.AddFailure(message);
                            // throw new ValidationException(message);
                        }
                        catch (NotImplementedException exc)
                        {
                            _ = exc;
                            ctx.AddFailure($"WASM Instruction `{inst.Op.GetMnemonic()}` is not implemented.");
                        }
                    });
            }
        }
    }
}