using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

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
        IInstruction ImmediateI32(int value);
        IInstruction Immediate(FuncIdx value);
        IInstruction Immediate(MemIdx value);
        IInstruction Immediate(ElemIdx value);
        IInstruction Immediate(DataIdx value);
        IInstruction Immediate(TableIdx x, ElemIdx y);
    }
    
}