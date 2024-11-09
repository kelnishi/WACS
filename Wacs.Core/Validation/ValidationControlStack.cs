using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public class ValidationControlFrame
    {
        public ByteCode Opcode { get; set; }
        public FunctionType Types { get; set; } = null!;

        public ResultType StartTypes => Types.ParameterTypes;
        public ResultType EndTypes => Types.ResultType;
        public ResultType LabelTypes => Opcode == OpCode.Loop ? StartTypes : EndTypes;
        public int Height { get; set; }

        //public bool ConditionallyReachable { get; set; }
        public bool Unreachable { get; set; }
    }
}