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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Reference
{
    public class InstBrOnNull : InstructionBase
    {
        private LabelIdx L;
        public override ByteCode Op => OpCode.BrOnNull;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction {0} invalid. Could not branch to label {1}",Op.GetMnemonic(), L);

            var refType = context.OpStack.PopRefType();
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);
            
            //Push the ref back for the else case.
            context.OpStack.PushRef(refType);
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
                InstBranch.ExecuteInstruction(context, L);
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
        public override ByteCode Op => OpCode.BrOnNonNull;
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.ContainsLabel(L.Value),
                "Instruction {0} invalid. Could not branch to label {1}",Op.GetMnemonic(), L);

            var refType = context.OpStack.PopRefType();
            
            var nthFrame = context.ControlStack.PeekAt((int)L.Value);
            //Pop values like we branch
            context.OpStack.DiscardValues(nthFrame.LabelTypes);
            //But actually, we don't, so push them back on.
            context.OpStack.PushResult(nthFrame.LabelTypes);
            
            //Push the ref back for the branch case.
            context.OpStack.PushRef(refType);
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
                InstBranch.ExecuteInstruction(context, L);
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
        public FuncIdx X;

        public InstCallRef()
        {
            IsAsync = true;
        }

        public override ByteCode Op => OpCode.Call;

        public bool IsBound(ExecContext context)
        {
            var a = context.Frame.Module.FuncAddrs[X];
            var func = context.Store[a];
            return func is HostFunction;
        }

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Funcs.Contains(X),
                "Instruction call_ref was invalid. Function {0} was not in the Context.",X);
            var func = context.Funcs[X];

            var refVal = context.OpStack.PopRefType();
            context.Assert(refVal.IsRef,
                "Instruction call_ref was invalid. Not a RefType. {0}", refVal);
            
            var type = context.Types[func.TypeIndex].CmpType;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction call_ref was invalid. Not a FuncType. {0}", type);
            
            context.OpStack.DiscardValues(funcType.ParameterTypes);
            context.OpStack.PushResult(funcType.ResultType);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsFuncRef,
                $"Instruction {Op.GetMnemonic()} failed. Expected FuncRef on top of stack.");
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");

            var r = context.OpStack.PopRefType();
            if (r.IsNullRef)
                throw new TrapException($"Null reference in call_ref");
            
            
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call_ref failed. Function Type {X} was not in the Context.");
            var xAddr = context.Frame.Module.FuncAddrs[X];
            var xInst = context.Store[xAddr];
            
            var a = (FuncAddr)r;
            context.Assert( context.Store.Contains(a),
                $"Instruction call_ref failed. Invalid Function Reference {r}.");
            var funcInst = context.Store[a];
            var ftActual = funcInst.Type;
            if (!xInst.Type.Matches(ftActual))
                throw new TrapException($"Instruction call_ref failed. Expected FunctionType differed.");
            
            context.Invoke(a);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsFuncRef,
                $"Instruction {Op.GetMnemonic()} failed. Expected FuncRef on top of stack.");
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");

            var r = context.OpStack.PopRefType();
            if (r.IsNullRef)
                throw new TrapException($"Null reference in call_ref");
            
            
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call_ref failed. Function Type {X} was not in the Context.");
            var xAddr = context.Frame.Module.FuncAddrs[X];
            var xInst = context.Store[xAddr];
            
            var a = (FuncAddr)r;
            context.Assert( context.Store.Contains(a),
                $"Instruction call_ref failed. Invalid Function Reference {r}.");
            var funcInst = context.Store[a];
            var ftActual = funcInst.Type;
            if (!xInst.Type.Matches(ftActual))
                throw new TrapException($"Instruction call_ref failed. Expected FunctionType differed.");

            await context.InvokeAsync(a);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (FuncIdx)reader.ReadLeb128_u32();
            return this;
        }

        public InstructionBase Immediate(FuncIdx value)
        {
            X = value;
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null)
            {
                var a = context.Frame.Module.FuncAddrs[X];
                var func = context.Store[a];
                if (!string.IsNullOrEmpty(func.Id))
                {
                    StringBuilder sb = new();
                    if (context.Attributes.Live)
                    {
                        sb.Append(" ");
                        var values = new Stack<Value>();
                        context.OpStack.PopResults(func.Type.ParameterTypes, ref values);
                        sb.Append("[");
                        while (values.Count > 0)
                        {
                            sb.Append(values.Peek().ToString());
                            if (values.Count > 1)
                                sb.Append(" ");
                            context.OpStack.PushValue(values.Pop());
                        }
                        sb.Append("]");
                    }
                    
                    return $"{base.RenderText(context)} {X.Value} (; -> {func.Id}{sb};)";
                }
            }
            return $"{base.RenderText(context)} {X.Value}";
        }
    }
    
}