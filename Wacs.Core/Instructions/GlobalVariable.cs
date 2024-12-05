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

using System;
using System.IO;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

// 5.4.4 Variable Instructions
namespace Wacs.Core.Instructions
{
    public class InstGlobalGet : InstructionBase, IContextConstInstruction, IVarInstruction, ITypedValueProducer<Value>
    {
        private GlobalIdx Index;
        public override ByteCode Op => OpCode.GlobalGet;

        public bool IsConstant(IWasmValidationContext? context) => 
            context == null || context.Globals.Contains(Index) && context.Globals[Index].IsImport && context.Globals[Index].Type.Mutability == Mutability.Immutable;

        public Func<ExecContext, Value> GetFunc => FetchFromGlobals;

        public int CalculateSize() => 1;

        public override InstructionBase Parse(BinaryReader reader)
        {
            Index = (GlobalIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
                return $"{base.RenderText(context)} {Index.Value}";
            if (!context.Attributes.Live)
                return $"{base.RenderText(context)} {Index.Value}";
            if (!context.Frame.Module.GlobalAddrs.Contains(Index))
                return $"{base.RenderText(context)} {Index.Value}";
            
            var a = context.Frame.Module.GlobalAddrs[Index];
            var glob = context.Store[a];
            var val = glob.Value;
            string valStr = $" (;>{val}<;)";
            return $"{base.RenderText(context)} {Index.Value}{valStr}";
        }

        //0x23
        // @Spec 3.3.5.4. global.get
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Globals.Contains(Index),
                "Instruction global.get was invalid. Context Globals did not contain {0}",Index);
            var globalType = context.Globals[Index].Type;
            context.OpStack.PushType(globalType.ContentType);
        }

        // @Spec 4.4.5.4. global.get
        public override void Execute(ExecContext context)
        {
            var val = FetchFromGlobals(context);
            context.OpStack.PushValue(val);
        }

        public Value FetchFromGlobals(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.GlobalAddrs.Contains(Index),
                "Runtime Globals did not contain address for {Index} in global.get");
            //3.
            var a = context.Frame.Module.GlobalAddrs[Index];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Runtime Store did not contain Global at address {a} in global.get");
            //5.
            var glob = context.Store[a];
            //6.
            var val = glob.Value;
            //7.
            return val;
        }
    }
    
    public class InstGlobalSet : InstructionBase, IContextConstInstruction, IVarInstruction, INodeConsumer<Value>
    {
        private GlobalIdx Index;

        public override ByteCode Op => OpCode.GlobalSet;

        public bool IsConstant(IWasmValidationContext? context) => 
            context == null || context.Globals.Contains(Index) && context.Globals[Index].IsImport && context.Globals[Index].Type.Mutability == Mutability.Immutable;

        public Action<ExecContext, Value> GetFunc => SetGlobal;

        public override InstructionBase Parse(BinaryReader reader)
        {
            Index = (GlobalIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
                return $"{base.RenderText(context)} {Index.Value}";
            if (!context.Attributes.Live)
                return $"{base.RenderText(context)} {Index.Value}";
            if (!context.Frame.Module.GlobalAddrs.Contains(Index))
                return $"{base.RenderText(context)} {Index.Value}";
            
            var a = context.Frame.Module.GlobalAddrs[Index];
            var glob = context.Store[a];
            var val = glob.Value;
            string valStr = $" (;>{val}<;)";
            return $"{base.RenderText(context)} {Index.Value}{valStr}";
        }

        //0x24
        // @Spec 3.3.5.5. global.set
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Globals.Contains(Index),
                "Instruction global.set was invalid. Context Globals did not contain {0}",Index);
            var global = context.Globals[Index];
            var mut = global.Type.Mutability;
            context.Assert(mut == Mutability.Mutable,
                "Instruction global.set was invalid. Trying to set immutable global {0}",Index);
            context.OpStack.PopType(global.Type.ContentType);

        }

        // @Spec 4.4.5.5. global.set
        public override void Execute(ExecContext context)
        {
            //2.
            context.Assert( context.Frame.Module.GlobalAddrs.Contains(Index),
                "Runtime Globals did not contain address for {Index} in global.set");
            //3.
            var a = context.Frame.Module.GlobalAddrs[Index];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Runtime Store did not contain Global at address {a} in global.set");
            //5.
            var glob = context.Store[a];
            //6.
            context.Assert( context.OpStack.HasValue,
                $"Operand Stack underflow in global.set");
            //7.
            var val = context.OpStack.PopType(glob.Type.ContentType);
            SetGlobal(context, val);
        }

        public void SetGlobal(ExecContext context, Value value)
        {
            //2.
            context.Assert( context.Frame.Module.GlobalAddrs.Contains(Index),
                "Runtime Globals did not contain address for {Index} in global.set");
            //3.
            var a = context.Frame.Module.GlobalAddrs[Index];
            //4.
            context.Assert( context.Store.Contains(a),
                $"Runtime Store did not contain Global at address {a} in global.set");
            //5.
            var glob = context.Store[a];
            
            //8.
            glob.Value = value;
        }
    }
    
}