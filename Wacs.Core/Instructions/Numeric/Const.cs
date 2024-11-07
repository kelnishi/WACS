using System.Globalization;
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    //0x41
    public class InstI32Const : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.I32Const;
        private int Value { get; set; }

        public bool IsConstant(IWasmValidationContext? ctx) => true;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushI32(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushI32(Value);

        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.ReadLeb128_s32();
            return this;
        }

        public IInstruction Immediate(int value)
        {
            Value = value;
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {Value}";
    }
    
    //0x42
    public class InstI64Const : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.I64Const;
        private long Value { get; set; }

        public bool IsConstant(IWasmValidationContext? ctx) => true;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushI64(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushI64(Value);

        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.ReadLeb128_s64();
            return this;
        }

        public override string RenderText(ExecContext? context) => $"{base.RenderText(context)} {Value}";
    }
    
    //0x43
    public class InstF32Const : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.F32Const;
        private float Value { get; set; }

        public bool IsConstant(IWasmValidationContext? ctx) => true;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushF32(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushF32(Value);

        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.Read_f32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            var sourceText = Value.ToString(CultureInfo.InvariantCulture).ToLower();
            if (sourceText.Contains("e-") || sourceText.Contains("e+") || sourceText.Length > 5)
                sourceText = Value.ToString("0.#####e+00");
            var floatText = FloatFormatter.FormatFloat(Value);
            return
                $"{base.RenderText(context)} {floatText} (;={sourceText};)";
        }
    }
    
    //0x44
    public class InstF64Const : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => OpCode.F64Const;
        private double Value { get; set; }

        public bool IsConstant(IWasmValidationContext? ctx) => true;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushF64(Value);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override void Execute(ExecContext context) =>
            context.OpStack.PushF64(Value);

        public override IInstruction Parse(BinaryReader reader) {
            Value = reader.Read_f64();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            var sourceText = Value.ToString(CultureInfo.InvariantCulture).ToLower();
            if (sourceText.Contains("e-") || sourceText.Contains("e+") || sourceText.Length > 5)
                sourceText = Value.ToString("0.#####e+00");
            var doubleText = FloatFormatter.FormatDouble(Value);
            return
                $"{base.RenderText(context)} {doubleText} (;={sourceText};)";
        }
    }
}