using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 3.1.1. Contexts
    /// </summary>
    public class WasmValidationContext
    {
        private readonly ValidationOpStack _stack = new();
        private readonly UnreachableOpStack _stackPolymorphic = new();
        public IValidationOpStack OpStack => Reachability ? _stack : _stackPolymorphic;
        public ValidationControlStack ControlStack { get; private set; } = new();
        
        public ResultType? Return { get; set; } = null;
        
        private ModuleInstance ValidationModule { get; set; }

        public TypesSpace Types => ValidationModule.Types;
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals => ControlStack.Frame.Locals;
        public ElementsSpace Elements { get; private set; }
        public DataValidationSpace Datas { get; private set; }

        private bool _reachability;

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
        
        /// <summary>
        /// @Spec 3.1.1. Contexts
        /// @Spec 3.4.10. Modules
        /// </summary>
        public WasmValidationContext(Module module)
        {
            ValidationModule = new ModuleInstance(module);
            Funcs = new FunctionsSpace(module);
            Tables = new TablesSpace(module);
            Mems = new MemSpace(module);

            Elements = new ElementsSpace(module.Elements.ToList());
            Datas = new DataValidationSpace(module.Datas.Length);

            Globals = new GlobalValidationSpace(module);

            Reachability = true;
        }
        
        public delegate string MessageProducer();

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
                            ctx.AddFailure($"WASM Instruction `{inst.OpCode.GetMnemonic()}` is not implemented.");
                        }
                    });
            }
        }
    }
    
}