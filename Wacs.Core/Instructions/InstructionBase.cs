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

        public abstract void Validate(IWasmValidationContext context);

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

        public virtual string RenderText(ExecContext? context) => Op.GetMnemonic();
    }
}