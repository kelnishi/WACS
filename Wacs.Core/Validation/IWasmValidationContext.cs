using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public interface IWasmValidationContext
    {
        public IValidationOpStack OpStack { get; }

        public TypesSpace Types { get; }

        public ValidationControlStack ControlStack { get; }

        public bool Reachability { get; set; }

        public IValidationOpStack ReturnStack { get; }

        public FunctionsSpace Funcs { get; }
        public TablesSpace Tables { get; }
        public MemSpace Mems { get; }
        public GlobalValidationSpace Globals { get; }
        public LocalsSpace Locals { get; }
        public ElementsSpace Elements { get; set; }
        public DataValidationSpace Datas { get; set; }

        public void Assert(bool factIsTrue, WasmValidationContext.MessageProducer message);

        public void NewOpStack(ResultType parameters);

        public void ValidateBlock(Block instructionBlock, int index = 0);

        public void FreeOpStack(ResultType results);
    }
}