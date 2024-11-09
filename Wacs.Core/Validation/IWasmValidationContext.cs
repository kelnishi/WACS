using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public interface IWasmValidationContext
    {
        public IValidationOpStack OpStack { get; }

        public Stack<ValidationControlFrame> ControlStack { get; }

        public FuncIdx FunctionIndex { get; }

        //Reference to the top of the control stack
        public ValidationControlFrame ControlFrame { get; }
        public ResultType ReturnType { get; }
        public bool Unreachable { get; set; }

        public TypesSpace Types { get; }
        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals { get; }
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }
        public bool ContainsLabel(uint label);
        public void PushControlFrame(ByteCode opCode, FunctionType types);
        public ValidationControlFrame PopControlFrame();
        public void SetUnreachable();

        public void Assert(bool factIsTrue, string message);
        public void Assert([NotNull] object? objIsNotNull, string message);
        public void ValidateBlock(Block instructionBlock, int index = 0);
    }
}