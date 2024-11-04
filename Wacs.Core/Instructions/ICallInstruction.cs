using Wacs.Core.Runtime;

namespace Wacs.Core.Instructions
{
    public interface ICallInstruction
    {
        public bool IsBound(ExecContext context);
    }
}