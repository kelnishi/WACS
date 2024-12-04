using System;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public abstract class InstFusedI64Const : InstructionBase, ITypedValueProducer<long>
    {
        protected readonly Func<ExecContext, long> _previous;
        protected long _constant;
        protected Func<ExecContext, long> _execute = null!;

        protected InstFusedI64Const(ITypedValueProducer<long> prev, long constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        static NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64);
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushI64(_execute(context));
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, long> GetFunc => _execute;
    }

    public class InstFusedI64Add : InstFusedI64Const
    {
        public override ByteCode Op => WacsCode.I64FusedAdd;
        public InstFusedI64Add(ITypedValueProducer<long> prev, long constant) : base(prev, constant) => 
            _execute = context => _previous(context) + _constant;
    }
    
    public class InstFusedI64Sub : InstFusedI64Const
    {
        public override ByteCode Op => WacsCode.I64FusedSub;
        public InstFusedI64Sub(ITypedValueProducer<long> prev, long constant) : base(prev, constant) => 
            _execute = context => _previous(context) - _constant;
    }
    
    public class InstFusedI64Mul : InstFusedI64Const
    {
        public override ByteCode Op => WacsCode.I64FusedMul;
        public InstFusedI64Mul(ITypedValueProducer<long> prev, long constant) : base(prev, constant) => 
            _execute = context => _previous(context) * _constant;
    }
    
    public abstract class InstFusedU64Const : InstructionBase, ITypedValueProducer<ulong>
    {
        protected readonly Func<ExecContext, ulong> _previous;
        protected ulong _constant;
        protected Func<ExecContext, ulong> _execute = null!;

        protected InstFusedU64Const(ITypedValueProducer<ulong> prev, ulong constant)
        {
            _previous = prev.GetFunc;
            _constant = constant;
            Size = prev.CalculateSize() + 1;
        }

        static NumericInst.ValidationDelegate _validate = NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64);
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        
        public override void Execute(ExecContext context)
        {
            context.OpStack.PushU64(_execute(context));
        }

        public int CalculateSize() => Size;
        public Func<ExecContext, ulong> GetFunc => _execute;
    }
    
    public class InstFusedU64And : InstFusedU64Const
    {
        public override ByteCode Op => WacsCode.I64FusedAnd;
        public InstFusedU64And(ITypedValueProducer<ulong> prev, ulong constant) : base(prev, constant) => 
            _execute = context => _previous(context) & _constant;
    }
    
    public class InstFusedU64Or : InstFusedU64Const
    {
        public override ByteCode Op => WacsCode.I64FusedOr;
        public InstFusedU64Or(ITypedValueProducer<ulong> prev, ulong constant) : base(prev, constant) => 
            _execute = context => _previous(context) | _constant;
    }
}