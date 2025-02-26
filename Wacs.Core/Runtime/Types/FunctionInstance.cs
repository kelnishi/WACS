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
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using InstructionPointer = System.Int32;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a WebAssembly-defined function instance.
    /// </summary>
    public class FunctionInstance : IFunctionInstance
    {
        private static Stack<Value> _asideVals = new();

        private readonly static ByteCode LabelInst = OpCode.Func;

        /// <summary>
        /// The function definition containing the raw code and locals.
        /// </summary>
        public readonly Module.Function Definition;

        public readonly DefType DefType;

        public readonly FuncIdx Index;

        public readonly ModuleInstance Module;

        //Copied from the static Definition
        //Can be processed with optimization passes
        public Expression Body;
        public int Length;
        public int MaxStack;

        public InstructionPointer LinkedOffset;

        //Copied from the static Definition
        public ValType[] Locals;

        /// <summary>
        /// @Spec 4.5.3.1. Functions
        /// Initializes a new instance of the <see cref="FunctionInstance"/> class.
        /// </summary>
        public FunctionInstance(ModuleInstance module, Module.Function definition)
        {
            var type = module.Types[definition.TypeIndex];
            if (!(type.Expansion is FunctionType funcType))
                throw new FormatException($"Function defined with type {type}");
            
            Type = funcType;
            Module = module;
            DefType = type;
            Definition = definition;
            Body = definition.Body;
            Body.LabelTarget.Label.Arity = Type.ResultType.Arity;
            Locals = definition.Locals;
            Index = definition.Index;
            
            if (!string.IsNullOrEmpty(Definition.Id))
                Name = Definition.Id;
        }

        public string ModuleName => Module.Name;
        public string Name { get; set; } = "";
        public FunctionType Type { get; }
        public void SetName(string value) => Name = value;
        public string Id => string.IsNullOrEmpty(Name)?"":$"{ModuleName}.{Name}";
        public bool IsExport { get; set; }

        public bool IsAsync => false;

        /// <summary>
        /// Sets Body and precomputes labels
        /// </summary>
        /// <param name="body"></param>
        public void SetBody(Expression body)
        {
            Body = body;
            Body.LabelTarget.Label.Arity = Type.ResultType.Arity;
        }

        public void Invoke(ExecContext context)
        {
            //3.
            var funcType = Type;
            //4.
            var t = Locals;
            //5. *Instructions will be handled in EnterSequence below
            //var seq = Body.Instructions;
            //6.
#if STRICT_EXECUTION
            context.Assert( context.OpStack.Count >= funcType.ParameterTypes.Arity,
                $"Function invocation failed. Operand Stack underflow.");
            //7.
            context.Assert(_asideVals.Count == 0,
                $"Shared temporary stack had values left in it.");
#endif
            //8.
            //Push the frame and operate on the frame on the stack.
            var frame = context.ReserveFrame(Module, funcType, Index, t);
            int parameterCount = funcType.ParameterTypes.Arity;
            int localCount = t.Length;
            int totalCount = parameterCount + localCount;
            //Load parameters
            // int li = context.OpStack.PopResults(funcType.ParameterTypes, ref frame.Locals.Data);
            // frame.StackHeight -= li;

            frame.Locals = context.OpStack.ReserveLocals(parameterCount, totalCount);
            context.OpStack.GuardExhaust(MaxStack);
                
            //Return the stack to this height after the function returns
            frame.StackHeight += localCount;
            
            //Set the Locals to default
            var slice = frame.Locals.Span[parameterCount..totalCount];
            for (int ti = 0; ti < localCount; ti++)
            {
                slice[ti] = new Value(t[ti]);
            }

            //9.
            context.PushFrame(frame);
            
            //10.
            frame.ReturnLabel.Arity = funcType.ResultType.Arity;
            frame.ReturnLabel.Instruction = LabelInst;
            frame.ReturnLabel.ContinuationAddress = context.GetPointer();
            frame.Head = LinkedOffset;
            
            // frame.SetLabel(Body.LabelTarget); 
            
            context.InstructionPointer = LinkedOffset - 1;
        }
        
        public void TailInvoke(ExecContext context)
        {
            var frame = context.ReuseFrame();
            //3.
            var funcType = Type;
            //4.
            var t = Locals;
            
            frame.Module = Module;
            frame.Type = funcType;
            frame.Index = Index;
            
            int parameterCount = funcType.ParameterTypes.Arity;
            
            int lastLocalsCount = frame.Locals.Length;
            int resultsHeight = frame.StackHeight - lastLocalsCount + parameterCount;
            context.OpStack.ShiftResults(parameterCount, resultsHeight);
            
            int localsCount = t.Length;
            int totalCount = parameterCount + localsCount;
            frame.Locals = context.OpStack.ReserveLocals(parameterCount, totalCount);
            context.OpStack.GuardExhaust(MaxStack);
            
            //Return the stack to this height after the function returns
            frame.StackHeight = resultsHeight + localsCount;
            
            //Set the Locals to default
            var slice = frame.Locals.Span[parameterCount..totalCount];
            for (int ti = 0; ti < localsCount; ti++)
            {
                slice[ti] = new Value(t[ti]);
            }
            
            //10.
            frame.ReturnLabel.Arity = funcType.ResultType.Arity;
            frame.ReturnLabel.Instruction = LabelInst;
            frame.Head = LinkedOffset;
            
            context.InstructionPointer = LinkedOffset - 1;
        }

        public override string ToString() => $"FunctionInstance[{Id}] (Type: {Type}, IsExport: {IsExport})";
    }
}