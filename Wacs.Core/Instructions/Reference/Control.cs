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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Reference
{
    public class InstBrOnNull : InstructionBase
    {
        private LabelIdx L;
        private BlockTarget? LinkedLabel;

        public override ByteCode Op => OpCode.BrOnNull;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction {0} invalid. Could not branch to label {1}",Op.GetMnemonic(), L);

            var refType = context.OpStack.PopRefType();       // -1
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);     // -(N+1)
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);        // -1
            
            //Push the non-null ref back for the else case.
            context.OpStack.PushRef(refType.ToConcrete());          // +0
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            LinkedLabel = InstBranch.PrecomputeStack(context, L);
            return base.Link(context, pointer);
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/#-hrefsyntax-instr-controlmathsfbr_on_nulll①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            var refVal = context.OpStack.PopRefType();
            if (refVal.IsNullRef)
            {
                InstBranch.ExecuteInstruction(context, LinkedLabel);
            }
            else
            {
                context.OpStack.PushRef(refVal);
            }
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstBrOnNonNull : InstructionBase
    {
        private LabelIdx L;
        private BlockTarget? LinkedLabel;

        public override ByteCode Op => OpCode.BrOnNonNull;
        public override int StackDiff => -1;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction {0} invalid. Could not branch to label {1}",Op.GetMnemonic(), L);

            var refType = context.OpStack.PopRefType();         // -1
            //Push the ref for the branch case.
            context.OpStack.PushRef(refType.ToConcrete());            // +0
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);       // -N
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);          // +0
            
            //Unpush the ref we pushed for the branch
            context.OpStack.PopRefType();                             // -1
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            LinkedLabel = InstBranch.PrecomputeStack(context, L);
            return base.Link(context, pointer);
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/#-hrefsyntax-instr-controlmathsfbr_on_non_nulll①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            var refVal = context.OpStack.PopRefType();
            if (!refVal.IsNullRef)
            {
                context.OpStack.PushRef(refVal);
                InstBranch.ExecuteInstruction(context, LinkedLabel);
            }
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            L = (LabelIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstCallRef : InstructionBase, ICallInstruction
    {
        public TypeIdx X;
        private FunctionType cachedFunctionType;

        public InstCallRef()
        {
            IsAsync = true;
        }

        public override ByteCode Op => OpCode.Call;

        public bool IsBound(ExecContext context)
        {
            return false;
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "Instruction call_ref was invalid. Function Type {0} was not in the Context.",X);
            var type = context.Types[X].Expansion;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction call_ref was invalid. Not a FuncType. {0}", type);
            
            var refVal = context.OpStack.PopRefType();          // -1
            context.Assert(refVal.IsRefType,
                "Instruction call_ref was invalid. Not a RefType. {0}", refVal);
            context.Assert(refVal.Type.IsNull() || refVal.Type.Index() == X,
                "Instruction call_ref was invalid. type mismatch: expected (ref null {0}), found {1}", X.Value, refVal.Type);
            context.OpStack.DiscardValues(funcType.ParameterTypes);   // -(N+1)
            context.OpStack.PushResult(funcType.ResultType);          // -N
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            context.Assert( context.Frame.Module.Types.Contains(X),
                $"Instruction call_ref failed to link. Function Type for {X} was not in the Context.");
            
            cachedFunctionType = (context.Frame.Module.Types[X].Expansion as FunctionType)!;
            
            context.LinkOpStackHeight -= 1;
            context.LinkOpStackHeight -= cachedFunctionType!.ParameterTypes.Arity;
            context.LinkOpStackHeight += cachedFunctionType.ResultType.Arity;
            return this;
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.StackTopTopType() == ValType.FuncRef,
                $"Instruction {Op.GetMnemonic()} failed. Expected FuncRef on top of stack.");
            var r = context.OpStack.PopRefType();
            if (r.IsNullRef)
                throw new TrapException($"Null reference in call_ref");
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            
            context.Assert( context.Store.Contains(a),
                $"Instruction call_ref failed. Invalid Function Reference {r}.");
            var funcInst = context.Store[a];
            var ftActual = funcInst.Type;
            if (!cachedFunctionType.Matches(ftActual, context.Frame.Module.Types))
                throw new TrapException($"Instruction call_ref failed. Expected FunctionType differed.");
            
            context.Invoke(a);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            context.Assert(context.StackTopTopType() == ValType.FuncRef,
                $"Instruction {Op.GetMnemonic()} failed. Expected FuncRef on top of stack.");
            var r = context.OpStack.PopRefType();
            if (r.IsNullRef)
                throw new TrapException($"Null reference in call_ref");
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            
            context.Assert( context.Store.Contains(a),
                $"Instruction call_ref failed. Invalid Function Reference {r}.");
            var funcInst = context.Store[a];
            var ftActual = funcInst.Type;
            if (!cachedFunctionType.Matches(ftActual, context.Frame.Module.Types))
                throw new TrapException($"Instruction call_ref failed. Expected FunctionType differed.");
            await context.InvokeAsync(a);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(TypeIdx value)
        {
            X = value;
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                var type = context.Frame.Module.Types[X];
                return $"{base.RenderText(context)} {type} {X.Value}";    
            }
            return $"{base.RenderText(context)} {X.Value}";
        }
    }
    
}