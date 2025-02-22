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
        public CastFlags Flags;
        public LabelIdx L;
        private BlockTarget? LinkedLabel;
        
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
            context.OpStack.PopType(Rt1);                       // -1
            context.OpStack.PushType(Rt2);                      // +0
            
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes); // -N
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);    // +0

            var rt2 = context.OpStack.PopAny();           // -1
            var diffType = rt2.Type.AsDiff(Rt1, context.Types);
            
            context.OpStack.PushType(diffType);                 // +0
        }
        
        public override InstructionBase Link(ExecContext context, int pointer)
        {
            LinkedLabel = InstBranch.PrecomputeStack(context, L);
            return base.Link(context, pointer);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack:{context.OpStack.Peek().Type}");
            var refVal = context.OpStack.PopRefType();
            
            context.OpStack.PushRef(refVal);

            if (Rt2.Matches(refVal, context.Frame.Module.Types))
                InstBranch.ExecuteInstruction(context, LinkedLabel);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            Flags = (CastFlags)reader.ReadByte();
            L = (LabelIdx)reader.ReadLeb128_u32();
            Rt1 = ValTypeParser.ParseHeapType(reader, Flags.HasFlag(CastFlags.NullEmpty));
            Rt2 = ValTypeParser.ParseHeapType(reader, Flags.HasFlag(CastFlags.EmptyNull));
            return this;
        }
    } 
    
    public class InstBrOnCastFail : InstructionBase
    {
        public CastFlags Flags;
        public LabelIdx L;
        private BlockTarget? LinkedLabel;
        
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
            context.OpStack.PopType(Rt1);                           // -1
            
            var diffType = Rt2.AsDiff(Rt1, context.Types);
            context.OpStack.PushType(diffType);                     // +0
            
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);     // -N
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);        // +0
            
            context.OpStack.PopAny();                               // -1
            context.OpStack.PushType(Rt2);                          // +0
        }
        
        public override InstructionBase Link(ExecContext context, int pointer)
        {
            LinkedLabel = InstBranch.PrecomputeStack(context, L);
            return base.Link(context, pointer);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack:{context.OpStack.Peek().Type}");
            var refVal = context.OpStack.PopRefType();
            context.OpStack.PushRef(refVal);
            if (!Rt2.Matches(refVal, context.Frame.Module.Types))
                InstBranch.ExecuteInstruction(context, LinkedLabel);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            Flags = (CastFlags)reader.ReadByte();
            L = (LabelIdx)reader.ReadLeb128_u32();
            Rt1 = ValTypeParser.ParseHeapType(reader, Flags.HasFlag(CastFlags.NullEmpty));
            Rt2 = ValTypeParser.ParseHeapType(reader, Flags.HasFlag(CastFlags.EmptyNull));
            return this;
        }
    }
}