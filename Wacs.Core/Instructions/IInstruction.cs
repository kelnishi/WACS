using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Represents a WebAssembly instruction.
    /// </summary>
    public interface IInstruction
    {
        public OpCode OpCode { get; }

        void Validate(WasmValidationContext context);
        
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
        /// Set a hard coded parameter for instructions created by the runtime
        /// </summary>
        IInstruction Immediate(int value);

        /// <summary>
        /// Set two hard coded parameters for instructions created by the runtime
        /// </summary>
        IInstruction Immediate(uint a, uint b);
    }
    
}