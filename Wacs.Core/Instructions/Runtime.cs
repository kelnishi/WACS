using FluentValidation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public class InstFuncReturn : InstructionBase
    {
        public override ByteCode Op => OpCode.Func;

        public override void Validate(IWasmValidationContext context)
        {
            throw new ValidationException($"This instruction should never be present in modules.");
        }

        public override void Execute(ExecContext context)
        {
            //Notify the runtime?
            context.RewindSequence();
        }
    }
}