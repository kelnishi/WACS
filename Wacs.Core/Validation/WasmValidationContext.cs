using System;
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

        private readonly ValidationOpStack _stack = new();
        private readonly UnreachableOpStack _stackPolymorphic = new();

        private bool _reachability;

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

            Reachability = true;
        }

        private ValidationContext<Module> RootContext { get; }

        public IValidationOpStack OpStack => Reachability ? _stack : _stackPolymorphic;
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
            get => _reachability;
            set
            {
                if (value && !_reachability)
                    _stack.Clear();
                _reachability = value;
            }
        }

        public void Assert(bool factIsTrue, MessageProducer message)
        {
            if (!factIsTrue)
                throw new InvalidDataException(message());
        }

        public void PushFrame(Module.Function func)
        {
            var funcType = Types[func.TypeIndex];
            var locals = new LocalsSpace(funcType.ParameterTypes.Types, func.Locals);
            var frame = new Frame(ValidationModule, funcType) { Locals = locals };
            ControlStack.PushFrame(frame);
        }

        public void PopFrame() => ControlStack.PopFrame();

        public void ValidateBlock(Block instructionBlock)
        {
            var blockValidator = new Block.Validator();
            var blockContext = RootContext.GetSubContext(instructionBlock);
            var blockResult = blockValidator.Validate(blockContext);
            foreach (var error in blockResult.Errors)
            {
                RootContext.AddFailure($"Block.{error.PropertyName}", error.ErrorMessage);
            }

            var instructionValidator = new InstructionValidator();
            foreach (var inst in instructionBlock.Instructions)
            {
                var subContext = RootContext.GetSubContext(inst);
                var result = instructionValidator.Validate(subContext);
                foreach (var error in result.Errors)
                {
                    RootContext.AddFailure($"Block Instruction.{error.PropertyName}", error.ErrorMessage);
                }
            }
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
                            ctx.AddFailure($"WASM Instruction `{inst.Op.GetMnemonic()}` is not implemented.");
                        }
                    });
            }
        }
    }
}