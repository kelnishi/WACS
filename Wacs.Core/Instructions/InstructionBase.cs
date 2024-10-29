using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// An abstract base class for all instruction implementations, providing common functionality.
    /// </summary>
    public abstract class InstructionBase : IInstruction
    {
        /// <summary>
        /// Gets the opcode associated with the instruction.
        /// </summary>
        public abstract ByteCode Op { get; }

        public abstract void Validate(WasmValidationContext context);

        /// <summary>
        /// Executes the instruction within the given execution context.
        /// </summary>
        /// <param name="context">The execution context in which to execute the instruction.</param>
        public abstract void Execute(ExecContext context);

        /// <summary>
        /// Instructions are responsible for parsing their binary representation.
        ///     This is called after the opcode has been read and before the operands are parsed.
        ///     Implementors should override this method to parse any operands that are not already present in the
        ///     binary representation of this instruction.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns>The parsed instruction</returns>
        public virtual IInstruction Parse(BinaryReader reader) => this;

        public virtual string RenderText(int depth) => Op.GetMnemonic();
    }
}