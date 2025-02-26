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

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.4 Variable Instructions
namespace Wacs.Core.Instructions
{
    public class InstLocalGet : InstructionBase, IVarInstruction, ITypedValueProducer<Value>
    {
        public InstLocalGet() : base(ByteCode.LocalGet, +1) { }
        
        private int Index;

        public int LinkStackDiff => StackDiff;
        
        public Func<ExecContext, Value> GetFunc => FetchFromLocals;

        public int CalculateSize() => 1;

        public int GetIndex() => Index;

        public override InstructionBase Parse(BinaryReader reader)
        {
            Index = (int)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(int index)
        {
            Index = index;
            return this;
        }

        //0x20
        // @Spec 3.3.5.1. local.get
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Locals.ContainsIndex(Index),
                "Instruction local.get was invalid. Context Locals did not contain variable at index {0}", Index);
            var value = context.Locals.Span[Index];
            
            context.Assert(value.Data.Set,
                "Instruction local.get was invalid. The non-defaultable local variable at index {0} was unset", Index);
            
            context.OpStack.PushType(value.Type);   // +1
        }

        public override void Execute(ExecContext context)
        {
            context.OpStack.PushValue(FetchFromLocals(context));
        }

        // @Spec 4.4.5.1. local.get 
        public Value FetchFromLocals(ExecContext context)
        {
            return context.Frame.Locals.Span[Index];
        }
    }
    
    public class InstLocalSet : InstructionBase, IVarInstruction, INodeConsumer<Value>
    {
        public InstLocalSet() : base(ByteCode.LocalSet, -1) { }
        
        private int Index;

        public int LinkStackDiff => StackDiff;
        
        public Action<ExecContext, Value> GetFunc => SetLocal;

        public int GetIndex() => (int)Index;

        public override InstructionBase Parse(BinaryReader reader)
        {
            Index = (int)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
                return $"{base.RenderText(context)} {Index}";
            if (!context.Attributes.Live)
                return $"{base.RenderText(context)} {Index}";
            if (!context.Frame.Locals.ContainsIndex(Index))
                return $"{base.RenderText(context)} {Index}";
            
            var value = context.Frame.Locals.Span[Index];
            string valStr = $" (;>{value}<;)";
            return $"{base.RenderText(context)} {Index}{valStr}";
        }

        //0x21
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Locals.ContainsIndex(Index),
                "Instruction local.set was invalid. Context Locals did not contain {0}",Index);
            context.Locals.Span[Index].Data.Set = true;
            var value = context.Locals.Span[Index];
            context.OpStack.PopType(value.Type);    // -1
        }

        // @Spec 4.4.5.2. local.set
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Locals.ContainsIndex(Index),
                $"Instruction local.set could not set Local {Index}");
            //3.
            context.Assert( context.OpStack.HasValue,
                $"Operand Stack underflow in instruction local.set");
            // var localValue = context.Frame.Locals.Get(Index);
            var localValue = context.Frame.Locals.Span[Index];
            var type = localValue.Type;
            //4.
            var value = context.OpStack.PopType(type);
            SetLocal(context, value);
        }

        public InstructionBase Immediate(int idx)
        {
            Index = idx;
            return this;
        }

        public void SetLocal(ExecContext context, Value value)
        {
            //5.
            // context.Frame.Locals.Set(Index, value);
            context.Frame.Locals.Span[Index] = value;
        }
    }
    
    public class InstLocalTee : InstructionBase, IVarInstruction
    {
        public InstLocalTee() : base(ByteCode.LocalTee) { }
        
        private int Index;
        
        public int GetIndex() => Index;

        public override InstructionBase Parse(BinaryReader reader)
        {
            Index = (int)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
                return $"{base.RenderText(context)} {Index}";
            if (!context.Attributes.Live)
                return $"{base.RenderText(context)} {Index}";
            if (!context.Frame.Locals.ContainsIndex(Index))
                return $"{base.RenderText(context)} {Index}";
            
            var value = context.Frame.Locals.Span[Index];
            string valStr = $" (;>{value}<;)";
            return $"{base.RenderText(context)} {Index}{valStr}";
        }

        //0x22
        // @Spec 3.3.5.2. local.tee
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Locals.ContainsIndex(Index),
                "Instruction local.tee was invalid. Context Locals did not contain {0}",Index);
            context.Locals.Span[Index].Data.Set = true;
            var value = context.Locals.Span[Index];
            context.OpStack.PopType(value.Type);    // -1
            context.OpStack.PushType(value.Type);   // +0
            context.OpStack.PushType(value.Type);   // +1
            context.OpStack.PopType(value.Type);    // +0
        }

        // @Spec 4.4.5.3. local.tee
        public override void Execute(ExecContext context)
        {
            //1.
            context.Assert( context.OpStack.HasValue,
                $"Operand Stack underflow in instruction local.tee");
            // var localValue = context.Frame.Locals.Get(Index);
            var reg = context.Frame.Locals.Span;
            var localValue = reg[Index];
            //2.
            var value = context.OpStack.PopType(localValue.Type);
            //3.
            context.OpStack.PushValue(value);
            //4.
            // context.OpStack.PushValue(value);
            //5.
            //Execute local.set (Collapse the push/pop)
            //2.
            context.Assert( context.Frame.Locals.ContainsIndex(Index),
                $"Instruction local.get could not get Local {Index}");
            //3.
            // context.Assert( context.OpStack.HasValue,
            //     $"Operand Stack underflow in instruction local.set");
            // var localValue = context.Frame.Locals.Get(Index);
            // var type = localValue.Type;
            //4.
            // var value = context.OpStack.PopType(type);
            //5.
            // context.Frame.Locals.Set(Index, value);
            reg[Index] = value;
        }
    }
}