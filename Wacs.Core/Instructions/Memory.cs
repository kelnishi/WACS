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
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

// 5.4.6 Memory Instructions
namespace Wacs.Core.Instructions
{
    public abstract class InstMemoryLoad : InstructionBase
    {
        private readonly ValType Type;
        private readonly BitWidth WidthT;
        protected readonly int WidthTByteSize;

        protected MemArg M;

        protected InstMemoryLoad(ValType type, BitWidth width, ByteCode opcode)
        {
            Type = type;
            WidthT = width;
            WidthTByteSize = WidthT.ByteSize();
            Op = opcode;
        }

        public override ByteCode Op { get; }

        /// <summary>
        /// @Spec 3.3.7.1. t.load
        /// @Spec 3.3.7.2. t.loadN_sx
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                 "Instruction {0} failed with invalid context memory 0.",Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= WidthTByteSize,
                    "Instruction {0} failed with invalid alignment {1} <= {2}/8",Op.GetMnemonic(),M.Align.LinearSize(),WidthT);

            context.OpStack.PopI32();
            context.OpStack.PushType(Type);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            return this;
        }

        public InstructionBase Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var loadedValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthT)} (;>{loadedValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthT)}";
        }

        public int CalculateSize() => 1;
    }

    public abstract class InstMemoryStore : InstructionBase
    {
        protected readonly ValType Type;
        private readonly BitWidth WidthT;
        protected readonly int WidthTByteSize;
        protected MemArg M;

        public InstMemoryStore(ValType type, BitWidth widthT, ByteCode opcode)
        {
            Type = type;
            WidthT = widthT;
            WidthTByteSize = WidthT.ByteSize();
            Op = opcode;
        }

        public override ByteCode Op { get; }

        /// <summary>
        /// @Spec 3.3.7.3. t.store
        /// @Spec 3.3.7.4. t.storeN
        /// </summary>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Mems.Contains(M.M),
                 "Instruction {0} failed with invalid context memory 0.",Op.GetMnemonic());
            context.Assert(M.Align.LinearSize() <= WidthT.ByteSize(),
                    "Instruction {0} failed with invalid alignment {1} <= {2}/8",Op.GetMnemonic(),M.Align.LinearSize(),WidthT);

            //Pop parameters from right to left
            context.OpStack.PopType(Type);
            context.OpStack.PopI32();
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            M = MemArg.Parse(reader);
            return this;
        }

        public InstructionBase Immediate(MemArg m)
        {
            M = m;
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                if (context.Attributes.Live && context.OpStack.Count > 0)
                {
                    var storeValue = context.OpStack.Peek();
                    return $"{base.RenderText(context)}{M.ToWat(WidthT)} (;>{storeValue}<;)";
                }
            }
            return $"{base.RenderText(context)}{M.ToWat(WidthT)}";
        }

        public int CalculateSize() => 1;
    }

}