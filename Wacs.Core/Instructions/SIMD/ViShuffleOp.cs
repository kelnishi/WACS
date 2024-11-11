using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public class InstShuffleOp : InstructionBase
    {
        private V128 X { get; set; }

        public override ByteCode Op { get; } // => SimdCode.I8x16Shuffle;

        public override void Validate(IWasmValidationContext context)
        {
            for (int i = 0; i < 16; ++i)
            {
                context.Assert(X[(byte)i] < 32,
                    $"Instruction {Op.GetMnemonic()} was invalid. Lane {i} ({X[(byte)i]}) was >= 32.");
            }

            context.OpStack.PopV128();
            context.OpStack.PopV128();
            context.OpStack.PushV128();
        }

        /// <summary>
        /// @Spec 4.4.3.7. i8x16.shuffle x
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new();
            for (byte i = 0; i < 16; ++i)
            {
                byte laneIndex = X[i];
                result[i] = laneIndex < 16 ? a[laneIndex] : b[(byte)(laneIndex - 16)];
            }
            context.OpStack.PushV128(result);
        }

        public static V128 ParseLanes(BinaryReader reader) => 
            new(reader.ReadBytes(16));

        public override IInstruction Parse(BinaryReader reader)
        {
            X = ParseLanes(reader);
            return this;
        }
    }
}