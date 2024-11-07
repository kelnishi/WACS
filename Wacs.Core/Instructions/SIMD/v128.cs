using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Simd
{
    //0x41
    public class InstV128Const : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => SimdCode.V128Const;
        private V128 V128 { get; set; }

        public bool IsConstant(IWasmValidationContext? ctx) => true;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushV128(V128);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushV128(V128);

        public override IInstruction Parse(BinaryReader reader)
        {
            V128 = reader.ReadBytes(16);
            return this;
        }

        public IInstruction Immediate(V128 value)
        {
            V128 = value;
            return this;
        }
    }
}