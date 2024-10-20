using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    //Inject this to change the instruction set
    public interface IInstructionFactory
    {
        T? CreateInstruction<T>(ByteCode code) where T : InstructionBase;
        IInstruction? CreateInstruction(ByteCode code);
    }
}