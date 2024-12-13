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
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.GC
{
    public class InstBrOnCast : InstructionBase
    {
        public LabelIdx L;
        private ValType Rt1;
        private ValType Rt2;
        
        public override ByteCode Op => GcCode.BrOnCast;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction {0} invalid. Could not branch to label {1}",Op.GetMnemonic(),L);
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            var rtp = Rt1.TopHeapType(context.Types);

            context.Assert(nthFrame.LabelTypes.Last().Matches(rtp, context.Types),
                "Instruction {0} invalid. Label {1} return type did not match {2}",Op.GetMnemonic(),L, rtp);
            context.Assert(Rt1.Validate(context.Types),
                "Instruction {0} invalid. Cast type {1} was invalid.",Op.GetMnemonic(), Rt1);
            context.Assert(Rt2.Validate(context.Types),
                    "Instruction {0} invalid. Cast type {1} was invalid.",Op.GetMnemonic(), Rt2);
            context.Assert(Rt2.Matches(Rt1, context.Types),
                "Instruction {0} invalid. Cast type {1} did not match {2}.",Op.GetMnemonic(), Rt2, Rt1);
            context.Assert(Rt2.Matches(rtp, context.Types),
                "Instruction {0} invalid. Cast type {1} did not match {2}.",Op.GetMnemonic(), Rt2, rtp);
            context.OpStack.PopType(Rt1);
            context.OpStack.PushType(Rt2);
            
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack:{context.OpStack.Peek().Type}");
            var refVal = context.OpStack.PopRefType();
            var rt1 = refVal.Type;
            refVal.Type = Rt2;
            context.OpStack.PushRef(refVal);

            if (rt1.Matches(Rt2, context.Frame.Module.Types))
                InstBranch.ExecuteInstruction(context, L);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            Rt1 = ValTypeParser.ParseRefType(reader);
            Rt1 = ValTypeParser.ParseRefType(reader);
            return this;
        }
    } 
    
    public class InstBrOnCastFail : InstructionBase
    {
        public LabelIdx L;
        private ValType Rt1;
        private ValType Rt2;
        
        public override ByteCode Op => GcCode.BrOnCastFail;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction {0} invalid. Could not branch to label {1}",Op.GetMnemonic(),L);
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            var rtp = Rt1.TopHeapType(context.Types);

            context.Assert(nthFrame.LabelTypes.Last().Matches(rtp, context.Types),
                "Instruction {0} invalid. Label {1} return type did not match {2}",Op.GetMnemonic(),L, rtp);
            context.Assert(Rt1.Validate(context.Types),
                "Instruction {0} invalid. Cast type {1} was invalid.",Op.GetMnemonic(), Rt1);
            context.Assert(Rt2.Validate(context.Types),
                    "Instruction {0} invalid. Cast type {1} was invalid.",Op.GetMnemonic(), Rt2);
            context.Assert(Rt2.Matches(Rt1, context.Types),
                "Instruction {0} invalid. Cast type {1} did not match {2}.",Op.GetMnemonic(), Rt2, Rt1);
            context.Assert(Rt2.Matches(rtp, context.Types),
                "Instruction {0} invalid. Cast type {1} did not match {2}.",Op.GetMnemonic(), Rt2, rtp);
            context.OpStack.PopType(Rt1);
            context.OpStack.PushType(Rt2);
            
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack:{context.OpStack.Peek().Type}");
            var refVal = context.OpStack.PopRefType();
            var rt1 = refVal.Type;
            refVal.Type = Rt2;
            context.OpStack.PushRef(refVal);

            if (!rt1.Matches(Rt2, context.Frame.Module.Types))
                InstBranch.ExecuteInstruction(context, L);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            Rt1 = ValTypeParser.ParseRefType(reader);
            Rt1 = ValTypeParser.ParseRefType(reader);
            return this;
        }
    }
}