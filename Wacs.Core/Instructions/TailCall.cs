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

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
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
        private FunctionInstance _functionInstance;

        public InstReturnCall()
        {
            IsAsync = false;
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
            //Call
            context.Assert(context.Funcs.Contains(X),
                "Instruction returncall was invalid. Function {0} was not in the Context.",X);
            var func = context.Funcs[X];
            var type = context.Types[func.TypeIndex].Expansion;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction returncall was invalid. {0} is not a FuncType", type);
            
            context.Assert(funcType.ResultType.Matches(context.ReturnType, context.Types),
                "Instruction return_call_indirect was invalid. Mismatched result types: calltype:{0} returntype:{1}", funcType.ResultType.ToNotation(), context.ReturnType.ToNotation());
            
            context.OpStack.DiscardValues(funcType.ParameterTypes);
            context.OpStack.PushResult(funcType.ResultType);
            
            //Return
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            context.Assert( context.Frame.Module.FuncAddrs.Contains(X),
                $"Instruction call failed. Function address for {X} was not in the Context.");
            var linkedX = context.Frame.Module.FuncAddrs[X];
            var inst = context.Store[linkedX];

            switch (inst)
            {
                case FunctionInstance func:
                    _functionInstance = func;
                    break;
                case HostFunction: 
                    throw new InvalidDataException($"Host functions are invalid tail-call targets.");
            }

            var funcType = inst.Type;
            int stack = context.LinkOpStackHeight;
            context.LinkOpStackHeight -= funcType.ParameterTypes.Arity;
            context.LinkOpStackHeight += funcType.ResultType.Arity;
            //For recordkeeping
            StackDiff = context.LinkOpStackHeight - stack;
            
            context.LinkUnreachable = true;

            IsAsync = false;
            return this;
        }

        // @Spec 4.4.8.10. call
        public override void Execute(ExecContext context)
        {
            _functionInstance.TailInvoke(context);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            throw new WasmRuntimeException($"Tail-call optimization targets must be synchronous.");
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
                if (!r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types))
                    throw new TrapException($"Instruction call_indirect failed. Element was not a FuncRef");
                var a = r.GetFuncAddr(context.Frame.Module.Types);
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
            //Call Indirect
            context.Assert(context.Tables.Contains(X),
                "Instruction return_call_indirect was invalid. Table {0} was not in the Context.",X);
            var tableType = context.Tables[X];
            context.Assert(tableType.ElementType.Matches(ValType.FuncRef, context.Types),
                "Instruction return_call_indirect was invalid. Table type was not funcref");
            context.Assert(context.Types.Contains(Y),
                "Instruction return_call_indirect was invalid. Function type {0} was not in the Context.",Y);
            
            var type = context.Types[Y].Expansion;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction return_call_indirect was invalid. Not a FuncType. {0}", type);

            context.Assert(funcType.ResultType.Matches(context.ReturnType, context.Types),
                "Instruction return_call_indirect was invalid. Mismatched result types: calltype:{0} returntype:{1}", funcType.ResultType.ToNotation(), context.ReturnType.ToNotation());

            var at = tableType.Limits.AddressType;
            
            context.OpStack.PopType(at.ToValType());
            context.OpStack.DiscardValues(funcType.ParameterTypes);
            context.OpStack.PushResult(funcType.ResultType);
            
            //Return
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            context.Assert( context.Frame.Module.TableAddrs.Contains(X),
                $"Instruction call_indirect failed. Table {X} was not in the Context.");
            var ta = context.Frame.Module.TableAddrs[X];
            context.Assert( context.Store.Contains(ta),
                $"Instruction call_indirect failed. TableInstance {ta} was not in the Store.");
            var tab = context.Store[ta];
            context.Assert( context.Frame.Module.Types.Contains(Y),
                $"Instruction call_indirect failed. Function Type {Y} was not in the Context.");
            var ftExpect = context.Frame.Module.Types[Y];
            var funcType = ftExpect.Expansion as FunctionType;
            context.Assert(funcType,
                $"Instruction {Op.GetMnemonic()} failed. Not a function type.");

            int stack = context.LinkOpStackHeight;
            context.LinkOpStackHeight -= funcType.ParameterTypes.Arity;
            context.LinkOpStackHeight += funcType.ResultType.Arity;
            
            //For recordkeeping
            StackDiff = context.LinkOpStackHeight - stack;
            
            return this;
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
            var ftFunc = ftExpect.Expansion as FunctionType;
            context.Assert(ftFunc,
                $"Instruction {Op.GetMnemonic()} failed. Not a function type.");
            
            //8.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopAddr();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            
            //18.
            //Check wasmfuncs, not hostfuncs
            if (funcInst is FunctionInstance ftAct)
                if (!ftAct.DefType.Matches(ftExpect, context.Frame.Module.Types))
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. RecursiveType differed.");
            if (!funcInst.Type.Matches(ftExpect.Unroll.Body, context.Frame.Module.Types))
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Expected FunctionType differed.");
            
            context.TailCall(a);
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
            var ftFunc = ftExpect.Expansion as FunctionType;
            context.Assert(ftFunc,
                $"Instruction {Op.GetMnemonic()} failed. Not a function type.");
            //8.
            context.Assert( context.OpStack.Peek().IsInt,
                $"Instruction {Op.GetMnemonic()} failed. Wrong type on stack.");
            //9.
            long i = context.OpStack.PopAddr();
            //10.
            if (i >= tab.Elements.Count)
                throw new TrapException($"Instruction call_indirect could not find element {i}");
            //11.
            var r = tab.Elements[(int)i];
            //12.
            if (r.IsNullRef)
                throw new TrapException($"Instruction call_indirect NullReference.");
            //13.
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            //14
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            //15.
            context.Assert( context.Store.Contains(a),
                $"Instruction call_indirect failed. Validation of table mutation failed.");
            //16.
            var funcInst = context.Store[a];
            //17.
            //18.
            //Check wasmfuncs, not hostfuncs
            if (funcInst is FunctionInstance ftAct)
                if (!ftAct.DefType.Matches(ftExpect, context.Frame.Module.Types))
                    throw new TrapException($"Instruction {Op.GetMnemonic()} failed. RecursiveType differed.");
            if (!funcInst.Type.Matches(ftExpect.Unroll.Body, context.Frame.Module.Types))
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Expected FunctionType differed.");
            
            context.TailCall(a);
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
                    if (!r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types))
                        throw new TrapException($"Instruction call_indirect failed. Element was not a FuncRef");
                    var a = r.GetFuncAddr(context.Frame.Module.Types);
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
    
    public class InstReturnCallRef : InstructionBase, ICallInstruction
    {
        public TypeIdx X;

        public InstReturnCallRef()
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
            //Call
            context.Assert(context.Types.Contains(X),
                "Instruction call_ref was invalid. Function Type {0} was not in the Context.",X);
            var type = context.Types[X].Expansion;
            var funcType = type as FunctionType;
            context.Assert(funcType,
                "Instruction call_ref was invalid. Not a FuncType. {0}", type);
            
            var refVal = context.OpStack.PopRefType();
            context.Assert(refVal.IsRefType,
                "Instruction call_ref was invalid. Not a RefType. {0}", refVal);
            context.Assert(refVal.Type.IsNull() || refVal.Type.Index() == X,
                "Instruction call_ref was invalid. type mismatch: expected (ref null {0}), found {1}", X.Value, refVal.Type);
            
            context.Assert(funcType.ResultType.Matches(context.ReturnType, context.Types),
                "Instruction return_call_ref was invalid. Mismatched result types: calltype:(func {0}){1} returntype:=(func {2}){3}",
                X.Value,
                funcType.ResultType.ToNotation(),
                context.FunctionIndex,
                context.ReturnType.ToNotation());
            
            context.OpStack.DiscardValues(funcType.ParameterTypes);
            context.OpStack.PushResult(funcType.ResultType);
            
            //Return
            Stack<Value> aside = new();
            context.OpStack.PopValues(context.ReturnType, ref aside);
            context.OpStack.PushValues(aside);
            context.SetUnreachable();
        }

        public override InstructionBase Link(ExecContext context, int pointer)
        {
            var funcType = context.Frame.Module.Types[X].Expansion as FunctionType;
            int stack = context.LinkOpStackHeight;
            context.LinkOpStackHeight -= 1;
            context.LinkOpStackHeight -= funcType!.ParameterTypes.Arity;
            context.LinkOpStackHeight += funcType.ResultType.Arity;
            
            //For recordkeeping
            StackDiff = context.LinkOpStackHeight - stack;
            
            return this;
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.StackTopTopType() == ValType.FuncRef,
                $"Instruction {Op.GetMnemonic()} failed. Expected FuncRef on top of stack.");
            context.Assert( context.Frame.Module.Types.Contains(X),
                $"Instruction return_call_ref failed. Function Type for {X} was not in the Context.");

            var r = context.OpStack.PopRefType();
            if (r.IsNullRef)
                throw new TrapException($"Null reference in call_ref");

            
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            
            context.Assert( context.Store.Contains(a),
                $"Instruction return_call_ref failed. Invalid Function Reference {r}.");
            
            var funcInst = context.Store[a];
            var ftActual = funcInst.Type;
            var funcType = context.Frame.Module.Types[X].Expansion as FunctionType;
            if (!funcType!.Matches(ftActual, context.Frame.Module.Types))
                throw new TrapException($"Instruction return_call_ref failed. Expected FunctionType differed.");
            
            context.TailCall(a);
        }

        public override async ValueTask ExecuteAsync(ExecContext context)
        {
            context.Assert(context.StackTopTopType() == ValType.FuncRef,
                $"Instruction {Op.GetMnemonic()} failed. Expected FuncRef on top of stack.");
            context.Assert( context.Frame.Module.Types.Contains(X),
                $"Instruction return_call_ref failed. Function Type for {X} was not in the Context.");

            var r = context.OpStack.PopRefType();
            if (r.IsNullRef)
                throw new TrapException($"Null reference in call_ref");

            
            context.Assert(r.Type.Matches(ValType.FuncRef, context.Frame.Module.Types),
                $"Instruction call_indirect failed. Element was not a FuncRef");
            var a = r.GetFuncAddr(context.Frame.Module.Types);
            
            context.Assert( context.Store.Contains(a),
                $"Instruction return_call_ref failed. Invalid Function Reference {r}.");
            
            var funcInst = context.Store[a];
            var ftActual = funcInst.Type;
            var funcType = context.Frame.Module.Types[X].Expansion as FunctionType;
            if (!funcType!.Matches(ftActual, context.Frame.Module.Types))
                throw new TrapException($"Instruction return_call_ref failed. Expected FunctionType differed.");
            
            context.TailCall(a);
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