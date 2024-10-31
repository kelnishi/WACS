using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Represents a WebAssembly instruction.
    /// </summary>
    public interface IInstruction
    {
        public ByteCode Op { get; }

        void Validate(IWasmValidationContext context);

        /// <summary>
        /// Executes the instruction within the given execution context.
        /// </summary>
        /// <param name="context">The execution context in which to execute the instruction.</param>
        void Execute(ExecContext context);

        /// <summary>
        /// Parses an instruction from a binary reader.
        /// </summary>
        IInstruction Parse(BinaryReader reader);

        /// <summary>
        /// Render the instruction at a given label stack depth
        /// </summary>
        string RenderText(ExecContext? context);

        public static bool IsEnd(IInstruction inst) => inst.Op.x00 == OpCode.End;

        public static bool IsElseOrEnd(IInstruction inst) => inst.Op.x00 switch
        {
            OpCode.Else => true,
            OpCode.End => true,
            _ => false
        };
    }
    
}