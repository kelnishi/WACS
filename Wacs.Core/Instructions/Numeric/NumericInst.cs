using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst : InstructionBase
    {
        public override OpCode OpCode { get; }
        
        public delegate void ExecuteDelegate(IExecContext context);
        private ExecuteDelegate _execute;
        public NumericInst(OpCode opCode, ExecuteDelegate execute) => _execute = execute;
        
        public override void Execute(IExecContext context) => _execute(context);
        public override IInstruction Parse(BinaryReader reader) => this;
    }
}