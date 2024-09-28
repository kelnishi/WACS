using System.Collections.Generic;
using Wacs.Core.Types;
using Wacs.Core.Runtime;

namespace Wacs.Core.Execution
{

    public class ExecContext
    {
        public IOperandStack Stack { get; private set; } = null!;

        public static ExecContext CreateExecContext() => new ExecContext {
            Stack = new ExecStack(),
        };

        public static ExecContext CreateValidationContext() => new ExecContext {
            Stack = new ValidationStack(),
        };
    }
    
}