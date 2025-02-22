// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.IO;
using System.Threading.Tasks;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Validation;
using InstructionPointer = System.Int32;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// An abstract base class for all instruction implementations, providing common functionality.
    /// </summary>
    public abstract class InstructionBase
    {
        public bool IsAsync = false;
        public int PointerAdvance = 0;

        public int Size = 1;

        protected virtual int StackDiff { get; set; }

        /// <summary>
        /// Gets the opcode associated with the instruction.
        /// </summary>
        public abstract ByteCode Op { get; }

        public abstract void Validate(IWasmValidationContext context);

        public virtual InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            context.LinkOpStackHeight += StackDiff;
            return this;
        }

        /// <summary>
        /// Synchronously executes the instruction within the given execution context.
        /// </summary>
        /// <param name="context">The execution context in which to execute the instruction.</param>
        /// <returns>The effective number of wasm instructions executed</returns>
        public abstract void Execute(ExecContext context);

        /// <summary>
        /// Asynchronously wraps the Execute function.
        /// Individual instructions may override to perform async functions.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>ValueTask containing the effective number of wasm instructions executed</returns>
        public virtual async ValueTask ExecuteAsync(ExecContext context)
        {
            throw new WasmRuntimeException("Async Execution must be explicitly implemented");
        }

        /// <summary>
        /// Instructions are responsible for parsing their binary representation.
        ///     This is called after the opcode has been read and before the operands are parsed.
        ///     Implementors should override this method to parse any operands that are not already present in the
        ///     binary representation of this instruction.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns>The parsed instruction</returns>
        public virtual InstructionBase Parse(BinaryReader reader) => this;

        /// <summary>
        /// Render the instruction at a given label stack depth
        /// </summary>
        public virtual string RenderText(ExecContext? context) => Op.GetMnemonic();

        public static bool IsEnd(InstructionBase inst) => inst.Op.x00 == OpCode.End;

        public static bool IsElseOrEnd(InstructionBase inst) => inst is InstEnd or InstElse;

        public static bool IsBranch(InstructionBase? inst) => inst is IBranchInstruction;

        public static bool IsNumeric(InstructionBase? inst) => inst is NumericInst;

        public static bool IsVar(InstructionBase? inst) => inst is IVarInstruction;

        public static bool IsLoad(InstructionBase? inst) => inst is InstMemoryLoad;

        public static bool IsBound(ExecContext context, InstructionBase? inst) => 
            inst is ICallInstruction ci && ci.IsBound(context);
    }
}