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

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public class InstReturnCall : InstructionBase, ICallInstruction
    {
        public FuncIdx X;

        public InstReturnCall()
        {
            IsAsync = true;
        }

        public override ByteCode Op => OpCode.ReturnCall;

        public bool IsBound(ExecContext context)
        {
            var a = context.Frame.Module.FuncAddrs[X];
            var func = context.Store[a];
            return func is HostFunction;
        }

        /// <summary>
        /// @Spec https://github.com/WebAssembly/tail-call/blob/main/proposals/tail-call/Overview.md
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            //Return
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
            
            //Call
            context.Assert(context.Funcs.Contains(X),
                "Instruction call was invalid. Function {0} was not in the Context.",X);
            var func = context.Funcs[X];
            var type = context.Types[func.TypeIndex];
            context.OpStack.PopValues(type.ParameterTypes, ref aside);
            aside.Clear();
            context.OpStack.PushResult(type.ResultType);
        }

        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            //Fetch the Module first because we might exhaust the call stack
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
            var a = context.Frame.Module.FuncAddrs[X];
            
            //Return
            // Split stack will preserve the operands, don't bother moving them.
            // Stack<Value> values = new Stack<Value>();
            // context.OpStack.PopResults(context.Frame.Type.ResultType, ref values);
            var address = context.PopFrame();
            //Push back operands
            // context.OpStack.Push(values);
            
            //Call
            context.Invoke(a);
            
            //Reuse the pointer from the outgoing function
            context.Frame.ContinuationAddress = address;
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            //Fetch the Module first because we might exhaust the call stack
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
            var a = context.Frame.Module.FuncAddrs[X];
            
            //Return
            // Split stack will preserve the operands, don't bother moving them.
            // Stack<Value> values = new Stack<Value>();
            // context.OpStack.PopResults(context.Frame.Type.ResultType, ref values);
            var address = context.PopFrame();
            //Push back operands
            // context.OpStack.Push(values);
            
            //Call
            await context.InvokeAsync(a);
            
            //Reuse the pointer from the outgoing function
            context.Frame.ContinuationAddress = address;
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            IsAsync = true;
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
                        Stack<Value> values = new Stack<Value>();
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

    //0x11
    public class InstReturnCallIndirect : InstructionBase, ICallInstruction
    {
        private TableIdx X;

        private TypeIdx Y;

        public InstReturnCallIndirect()
        {
            IsAsync = true;
        }

        public override ByteCode Op => OpCode.ReturnCallIndirect;

        public bool IsBound(ExecContext context)
        {
            try
            {
                var ta = context.Frame.Module.TableAddrs[X];
                var tab = context.Store[ta];
                int i = context.OpStack.Peek();
                if (i >= tab.Elements.Count)
                    throw new TrapException($"Instruction call_indirect could not find element {i}");
                var r = tab.Elements[i];
                if (r.IsNullRef)
                    throw new TrapException($"Instruction call_indirect NullReference.");
                var a = (FuncAddr)r;
                var func = context.Store[a];
                return func is HostFunction;
            }
            catch (TrapException)
            {
                return false;
            }
        }

        /// <summary>
        /// @Spec https://github.com/WebAssembly/tail-call/blob/main/proposals/tail-call/Overview.md
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            //Return
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
            
            //Call Indirect
            context.Assert(context.Tables.Contains(X),
                "Instruction call_indirect was invalid. Table {0} was not in the Context.",X);
            var tableType = context.Tables[X];
            context.Assert(tableType.ElementType == ValType.Func,
                "Instruction call_indirect was invalid. Table type was not funcref");
            context.Assert(context.Types.Contains(Y),
                "Instruction call_indirect was invalid. Function type {0} was not in the Context.",Y);
            var funcType = context.Types[Y];

            context.OpStack.PopI32();
            context.OpStack.PopValues(funcType.ParameterTypes, ref aside);
            aside.Clear();
            context.OpStack.PushResult(funcType.ResultType);
        }

        // @Spec 4.4.8.11. call_indirect
        public override void Execute(ExecContext context)
        {
            //Call Indirect
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction call_indirect failed. Table {X} was not in the Context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[X];
            //4.
            context.Assert( context.Store.Contains(ta),
                $"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            //5.
            var tab = context.Store[ta];
            //6.
            context.Assert( context.Frame.Module.Types.Contains(Y),
                $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            //7.
            var ftExpect = context.Frame.Module.Types[Y];
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            uint i = context.OpStack.PopU32();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert( r.Type == ValType.Func,
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14.
            var a = (FuncAddr)r;
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            var ftActual = funcInst.Type;
            //18.
            if (!ftExpect.Matches(ftActual))
                throw new TrapException($"Instruction call_indirect failed. Expected FunctionType differed.");
            
            //Return
            // Split stack will preserve the operands, don't bother moving them.
            // Stack<Value> values = new Stack<Value>();
            // context.OpStack.PopResults(context.Frame.Type.ResultType, ref values);
            var address = context.PopFrame();
            //Push back operands
            // context.OpStack.Push(values);
            
            //19.
            context.Invoke(a);
            
            //Reuse the pointer from the outgoing function
            context.Frame.ContinuationAddress = address;
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            //Call Indirect
            //2.
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction call_indirect failed. Table {X} was not in the Context.");
            //3.
            var ta = context.Frame.Module.TableAddrs[X];
            //4.
            context.Assert( context.Store.Contains(ta),
                $"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            //5.
            var tab = context.Store[ta];
            //6.
            context.Assert( context.Frame.Module.Types.Contains(Y),
                $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            //7.
            var ftExpect = context.Frame.Module.Types[Y];
            //8.
            context.Assert( context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            uint i = context.OpStack.PopU32();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert( r.Type == ValType.Func,
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14.
            var a = (FuncAddr)r;
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            var ftActual = funcInst.Type;
            //18.
            if (!ftExpect.Matches(ftActual))
                throw new TrapException($"Instruction call_indirect failed. Expected FunctionType differed.");
            
            //Return
            // Split stack will preserve the operands, don't bother moving them.
            // Stack<Value> values = new Stack<Value>();
            // context.OpStack.PopResults(context.Frame.Type.ResultType, ref values);
            var address = context.PopFrame();
            //Push back operands
            // context.OpStack.Push(values);
            
            //19.
            await context.InvokeAsync(a);
            
            //Reuse the pointer from the outgoing function
            context.Frame.ContinuationAddress = address;
        }

        /// <summary>
        /// @Spec 5.4.1 Control Instructions
        /// </summary>
        public override InstructionBase Parse(BinaryReader reader)
        {
            Y = (TypeIdx)reader.ReadLeb128_u32();
            X = (TableIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override string RenderText(ExecContext? context)
        {
            if (context != null && context.Attributes.Live)
            {
                try
                {
                    var ta = context.Frame.Module.TableAddrs[X];
                    var tab = context.Store[ta];
                    int i = context.OpStack.Peek();
                    if (i >= tab.Elements.Count)
                        throw new TrapException($"Instruction call_indirect could not find element {i}");
                    var r = tab.Elements[i];
                    if (r.IsNullRef)
                        throw new TrapException($"Instruction call_indirect NullReference.");
                    var a = (FuncAddr)r;
                    var func = context.Store[a];


                    if (!string.IsNullOrEmpty(func.Id))
                    {
                        StringBuilder sb = new();
                        if (context.Attributes.Live)
                        {
                            sb.Append(" ");
                            Stack<Value> values = new Stack<Value>();
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
                catch (TrapException)
                {
                    return $"{base.RenderText(context)}{(X.Value == 0 ? "" : $" {X.Value}")} (type {Y.Value})";
                }
            }
            
            return $"{base.RenderText(context)}{(X.Value == 0 ? "" : $" {X.Value}")} (type {Y.Value})";
        }
    }
}