using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public interface IConstInstruction
    {
        public bool IsConstant(IWasmValidationContext? context);
    }
}