using System;
using System.Collections.Generic;
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
    public class WasmValidationContext
    {
        public delegate string MessageProducer();

        private readonly UnreachableOpStack _stackPolymorphic = new();

        private readonly Stack<ValidationOpStack> _stacks = new();
        private Stack<IValidationContext> _contextStack = new();

        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// @Spec 3.4.10. Modules
        /// </summary>
        public WasmValidationContext(Module module, ValidationContext<Module> rootContext)
        {
            ValidationModule = new ModuleInstance(module);

            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = new ElementsSpace(module.Elements.ToList());
            Datas = new DataValidationSpace(module.Datas.Length);

            Globals = new GlobalValidationSpace(module);

            RootContext = rootContext;
            _contextStack.Push(rootContext);
            NewOpStack(ResultType.Empty);
            ReturnStack = Stack;
        }

        private ValidationOpStack Stack => _stacks.Peek();

        public ValidationContext<Module> RootContext { get; }

        public IValidationOpStack OpStack => Stack.Reachability ? Stack : _stackPolymorphic;

        public IValidationOpStack ReturnStack { get; }

        public ValidationControlStack ControlStack { get; } = new();

        public Frame Frame => ControlStack.Frame;

        public ResultType Return => Frame.Type.ResultType;

        private ModuleInstance ValidationModule { get; }

        public TypesSpace Types => ValidationModule.Types;
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals => ControlStack.Frame.Locals;
        public ElementsSpace Elements { get; private set; }
        public DataValidationSpace Datas { get; private set; }

        public bool Reachability
        {
            get => Stack.Reachability;
            set => Stack.Reachability = value;
        }

        public void NewOpStack(ResultType parameters)
        {
            _stacks.Push(new ValidationOpStack());

            foreach (var type in parameters.Types)
            {
                Stack.PushType(type);
            }
        }

        public void FreeOpStack(ResultType results)
        {
            if (_stacks.Count == 1)
                throw new InvalidOperationException($"Validation Operand Stack underflow");
            
            _stacks.Pop();
            
            foreach (var type in results.Types)
            {
                Stack.PushType(type);
            }
        }

        public void ClearOpStacks()
        {
            while (_stacks.Count > 1)
                _stacks.Pop();
            Stack.Clear();
        }

        public ValidationContext<T> PushSubContext<T>(T child, int index = -1)
        where T : class
        {
            var subctx = _contextStack.Peek().GetSubContext(child, index);
            _contextStack.Push(subctx);
            return subctx;
        }

        public void PopValidationContext() => _contextStack.Pop();

        public void Assert(bool factIsTrue, MessageProducer message)
        {
            if (!factIsTrue)
                throw new ValidationException(message());
        }

        public void PushFrame(Module.Function func)
        {
            var funcType = Types[func.TypeIndex];
            var locals = new LocalsSpace(funcType.ParameterTypes.Types, func.Locals);
            var frame = new Frame(ValidationModule, funcType) { Locals = locals };
            ControlStack.PushFrame(frame);
        }

        public void PopFrame() => ControlStack.PopFrame();

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
                            string path = ctx.PropertyPath;
                            int line = ctx.GetValidationContext().Frame.Module.Repr.CalculateLine(path, false, out var i);
                            
                            ctx.AddFailure($"{ctx.PropertyPath}: {exc.Message}");
                        }
                        catch (InvalidDataException exc)
                        {
                            ctx.AddFailure($"{ctx.PropertyPath}: Invalid instruction data; {exc.Message}");
                        }
                        catch (NotImplementedException exc)
                        {
                            _ = exc;
                            ctx.AddFailure($"{ctx.PropertyPath}: WASM Instruction `{inst.Op.GetMnemonic()}` is not implemented.");
                        }
                    });
            }
        }
    }
}