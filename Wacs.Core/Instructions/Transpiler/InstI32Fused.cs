using System;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public abstract class InstFusedI32Const : InstructionBase, ITypedValueProducer<int>
    {
        protected readonly Func<ExecContext, int> _previous;
        protected int _constant;
        protected Func<ExecContext, int> _execute = null!;

        protected InstFusedI32Const(ITypedValueProducer<int> prev, int constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        static NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32);
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI32(_execute(context));
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, int> GetFunc => _execute;
    }

    public class InstFusedI32Add : InstFusedI32Const
    {
        public override ByteCode Op => WacsCode.I32FusedAdd;
        public InstFusedI32Add(ITypedValueProducer<int> prev, int constant) : base(prev, constant) => 
            _execute = context => _previous(context) + _constant;
    }
    
    public class InstFusedI32Sub : InstFusedI32Const
    {
        public override ByteCode Op => WacsCode.I32FusedSub;
        public InstFusedI32Sub(ITypedValueProducer<int> prev, int constant) : base(prev, constant) => 
            _execute = context => _previous(context) - _constant;
    }
    
    public class InstFusedI32Mul : InstFusedI32Const
    {
        public override ByteCode Op => WacsCode.I32FusedMul;
        public InstFusedI32Mul(ITypedValueProducer<int> prev, int constant) : base(prev, constant) => 
            _execute = context => _previous(context) * _constant;
    }
    
    public abstract class InstFusedU32Const : InstructionBase, ITypedValueProducer<uint>
    {
        protected readonly Func<ExecContext, uint> _previous;
        protected uint _constant;
        protected Func<ExecContext, uint> _execute = null!;

        protected InstFusedU32Const(ITypedValueProducer<uint> prev, uint constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        static NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32);
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushU32(_execute(context));
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, uint> GetFunc => _execute;
    }
    
    public class InstFusedU32And : InstFusedU32Const
    {
        public override ByteCode Op => WacsCode.I32FusedAnd;
        public InstFusedU32And(ITypedValueProducer<uint> prev, uint constant) : base(prev, constant) => 
            _execute = context => _previous(context) & _constant;
    }
    
    public class InstFusedU32Or : InstFusedU32Const
    {
        public override ByteCode Op => WacsCode.I32FusedOr;
        public InstFusedU32Or(ITypedValueProducer<uint> prev, uint constant) : base(prev, constant) => 
            _execute = context => _previous(context) | _constant;
    }
}