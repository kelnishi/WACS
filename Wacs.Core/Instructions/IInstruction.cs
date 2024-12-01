// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System.IO;
using System.Threading.Tasks;
using Wacs.Core.Instructions.Numeric;
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
        /// Synchronously executes the instruction within the given execution context.
        /// </summary>
        /// <param name="context">The execution context in which to execute the instruction.</param>
        /// <returns>The effective number of wasm instructions executed</returns>
        void Execute(ExecContext context);

        /// <summary>
        /// Asynchronously wraps the Execute function.
        /// Individual instructions may override to perform async functions.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>ValueTask containing the effective number of wasm instructions executed</returns>
        public ValueTask ExecuteAsync(ExecContext context);

        /// <summary>
        /// Parses an instruction from a binary reader.
        /// </summary>
        IInstruction Parse(BinaryReader reader);

        /// <summary>
        /// Render the instruction at a given label stack depth
        /// </summary>
        string RenderText(ExecContext? context);

        public static bool IsEnd(IInstruction inst) => inst.Op.x00 == OpCode.End;

        public static bool IsElseOrEnd(IInstruction inst) => inst is InstEnd;

        public static bool IsBranch(IInstruction? inst) => inst is IBranchInstruction;

        public static bool IsNumeric(IInstruction? inst) => inst is NumericInst;

        public static bool IsVar(IInstruction? inst) => inst is IVarInstruction;

        public static bool IsLoad(IInstruction? inst) => inst is InstMemoryLoad;

        public static bool IsBound(ExecContext context, IInstruction? inst) => 
            inst is ICallInstruction ci && ci.IsBound(context);
    }
    
}