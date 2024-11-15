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

        public static bool IsBranch(IInstruction? inst) => inst?.Op.x00 switch
        {
            OpCode.Br => true,
            OpCode.BrIf => true,
            OpCode.BrTable => true,
            _ => false
        };

        public static bool IsNumeric(IInstruction? inst) => inst is NumericInst;

        public static bool IsVar(IInstruction? inst) =>
            inst switch
            {
                LocalVariableInst _ => true,
                GlobalVariableInst _ => true,
                _ => false,
            };

        public static bool IsLoad(IInstruction? inst) => inst is InstMemoryLoad;

        public static bool IsBound(ExecContext context, IInstruction? inst) => 
            inst is ICallInstruction ci && ci.IsBound(context);
    }
    
}