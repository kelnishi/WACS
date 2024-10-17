using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst : InstructionBase
    {
        public override OpCode OpCode { get; }

        public delegate void ValidationDelegate(WasmValidationContext context);
        public delegate void ExecuteDelegate(ExecContext context);

        private ValidationDelegate _validate;
        private ExecuteDelegate _execute;
        public NumericInst(OpCode opCode, ExecuteDelegate execute, ValidationDelegate validate) => 
            (_execute, _validate) = (execute, validate);

        public override void Validate(WasmValidationContext context) => _validate(context);
        public override void Execute(ExecContext context) => _execute(context);
        
        // [pop] -> [push]
        public static ValidationDelegate ValidateOperands(ValType pop, ValType push)
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
        public static ValidationDelegate ValidateOperands(ValType pop1, ValType pop2, ValType push)
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