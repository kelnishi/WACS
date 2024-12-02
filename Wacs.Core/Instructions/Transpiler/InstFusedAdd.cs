using System;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public class InstFusedI32AddS : InstructionBase, ITypedValueProducer<int>
    {
        private readonly Func<ExecContext, int> _previous;
        private int _constant;
        private int Size;
        public InstFusedI32AddS(ITypedValueProducer<int> prev, int constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        static NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32);
        public override ByteCode Op => WacsCode.I32FusedAdd;
        public override void Validate(IWasmValidationContext context) => _validate(context);
        
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI32(FusedAdd(context));
        }

        public int FusedAdd(ExecContext context) => _previous(context) + _constant;

        public Func<ExecContext, int> GetFunc => FusedAdd;
        public int CalculateSize() => Size;
    }
}