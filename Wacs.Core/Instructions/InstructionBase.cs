using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

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
        public abstract OpCode OpCode { get; }

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

        private const string ImmediateExcMessage = "Instruction did not implement Immediate parameter";
        /// <summary>
        /// When creating Instructions in the runtime, this can be called to load a parameter
        /// Prefer strongly typed immediates, throw if not implemented.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>The initialized instruction</returns>
        public virtual IInstruction ImmediateI32(int value) => throw new InvalidOperationException(ImmediateExcMessage);
        public virtual IInstruction Immediate(FuncIdx value) => throw new InvalidOperationException(ImmediateExcMessage);
        public virtual IInstruction Immediate(MemIdx value) => throw new InvalidOperationException(ImmediateExcMessage);
        public virtual IInstruction Immediate(ElemIdx value) => throw new InvalidOperationException(ImmediateExcMessage);
        public virtual IInstruction Immediate(DataIdx value) => throw new InvalidOperationException(ImmediateExcMessage);
        public virtual IInstruction Immediate(TableIdx x, ElemIdx y) => throw new InvalidOperationException(ImmediateExcMessage);
    }
}