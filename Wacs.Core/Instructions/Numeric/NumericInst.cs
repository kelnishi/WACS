using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst : InstructionBase
    {
        private readonly ExecuteDelegate _execute;

        private readonly ValidationDelegate _validate;

        private NumericInst(ByteCode op, ExecuteDelegate execute, ValidationDelegate validate) =>
            (Op, _execute, _validate) = (op, execute, validate);

        public override ByteCode Op { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context);
        public override void Execute(ExecContext context) => _execute(context);

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
                return $"{base.RenderText(context)}";
            if (!context.Attributes.Live)
                return $"{base.RenderText(context)}";

            var val = context.OpStack.PopAny();
            string valStr = $" (;>{val}<;)";
            context.OpStack.PushValue(val);
            return $"{base.RenderText(context)} {valStr}";
        }

        // [pop] -> [push]
        private static ValidationDelegate ValidateOperands(ValType pop, ValType push) =>
            context =>
            {
                context.OpStack.PopType(pop);
                context.OpStack.PushType(push);
            };

        // [pop1 pop2] -> [push]
        private static ValidationDelegate ValidateOperands(ValType pop1, ValType pop2, ValType push) =>
            context => {
                context.OpStack.PopType(pop2);
                context.OpStack.PopType(pop1);
                context.OpStack.PushType(push);
            };

        // [pop1 pop2 pop3] -> [push]
        private static ValidationDelegate ValidateOperands(ValType pop1, ValType pop2, ValType pop3, ValType push) =>
            context => {
                context.OpStack.PopType(pop3);
                context.OpStack.PopType(pop2);
                context.OpStack.PopType(pop1);
                context.OpStack.PushType(push);
            };

        private delegate void ExecuteDelegate(ExecContext context);

        private delegate void ValidationDelegate(IWasmValidationContext context);
    }
}