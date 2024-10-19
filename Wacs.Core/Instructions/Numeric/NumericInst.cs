using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst : InstructionBase
    {
        private delegate void ExecuteDelegate(ExecContext context);

        private delegate void ValidationDelegate(WasmValidationContext context);

        private readonly ExecuteDelegate _execute;

        private readonly ValidationDelegate _validate;

        private NumericInst(OpCode opCode, ExecuteDelegate execute, ValidationDelegate validate) => 
            (OpCode, _execute, _validate) = (opCode, execute, validate);

        public override OpCode OpCode { get; }

        public override void Validate(WasmValidationContext context) => _validate(context);
        public override void Execute(ExecContext context) => _execute(context);

        // [pop] -> [push]
        private static ValidationDelegate ValidateOperands(ValType pop, ValType push)
        {
            return (context) => {
                switch (pop) {
                    case ValType.I32: context.OpStack.PopI32(); break;
                    case ValType.I64: context.OpStack.PopI64(); break;
                    case ValType.F32: context.OpStack.PopF32(); break;
                    case ValType.F64: context.OpStack.PopF64(); break;
                    default: throw new InvalidOperationException($"Unsupported input type: {pop}");
                }
                switch (push) {
                    case ValType.I32: context.OpStack.PushI32(); break;
                    case ValType.I64: context.OpStack.PushI64(); break;
                    case ValType.F32: context.OpStack.PushF32(); break;
                    case ValType.F64: context.OpStack.PushF64(); break;
                    default: throw new InvalidOperationException($"Unsupported output type: {push}");
                }
            };
        }

        // [pop2 pop2] -> [push]
        private static ValidationDelegate ValidateOperands(ValType pop1, ValType pop2, ValType push)
        {
            return (context) => {
                switch (pop2) {
                    case ValType.I32: context.OpStack.PopI32(); break;
                    case ValType.I64: context.OpStack.PopI64(); break;
                    case ValType.F32: context.OpStack.PopF32(); break;
                    case ValType.F64: context.OpStack.PopF64(); break;
                    default: throw new InvalidOperationException($"Unsupported input type: {pop2}");
                }
                switch (pop1) {
                    case ValType.I32: context.OpStack.PopI32(); break;
                    case ValType.I64: context.OpStack.PopI64(); break;
                    case ValType.F32: context.OpStack.PopF32(); break;
                    case ValType.F64: context.OpStack.PopF64(); break;
                    default: throw new InvalidOperationException($"Unsupported input type: {pop1}");
                }
                switch (push) {
                    case ValType.I32: context.OpStack.PushI32(); break;
                    case ValType.I64: context.OpStack.PushI64(); break;
                    case ValType.F32: context.OpStack.PushF32(); break;
                    case ValType.F64: context.OpStack.PushF64(); break;
                    default: throw new InvalidOperationException($"Unsupported output type: {push}");
                }
            };
        }
    }
}