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
using FluentValidation;
using FluentValidation.Internal;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a WebAssembly-defined function instance.
    /// </summary>
    public class FunctionInstance : IFunctionInstance
    {
        /// <summary>
        /// @Spec 4.5.3.1. Functions
        /// Initializes a new instance of the <see cref="FunctionInstance"/> class.
        /// </summary>
        public FunctionInstance(ModuleInstance module, Module.Function definition)
        {
            Type = module.Types[definition.TypeIndex];
            Module = module;
            
            Definition = definition;
            SetBody(definition.Body);
            
            Locals = definition.Locals;
            Index = definition.Index;
            
            if (!string.IsNullOrEmpty(Definition.Id))
                Name = Definition.Id;
        }

        public readonly ModuleInstance Module;

        /// <summary>
        /// The function definition containing the raw code and locals.
        /// </summary>
        public readonly Module.Function Definition;

        //Copied from the static Definition
        //Can be processed with optimization passes
        public Expression Body;

        /// <summary>
        /// Sets Body and precomputes labels
        /// </summary>
        /// <param name="body"></param>
        public void SetBody(Expression body)
        {
            Body = body;
            Body.LabelTarget.Label.Arity = Type.ResultType.Arity;

            var vContext = new StackCalculator(Module, Definition);
            Body.PrecomputeLabels(vContext);
        }

        //Copied from the static Definition
        public ValType[] Locals;

        public readonly FuncIdx Index;

        public string ModuleName => Module.Name;
        public string Name { get; set; } = "";
        public FunctionType Type { get; }
        public void SetName(string value) => Name = value;
        public string Id => string.IsNullOrEmpty(Name)?"":$"{ModuleName}.{Name}";
        public bool IsExport { get; set; }

        private static Stack<Value> _asideVals = new();

        private readonly static ByteCode LabelInst = OpCode.Func; 
        
        public void Invoke(ExecContext context)
        {
            //3.
            var funcType = Type;
            //4.
            var t = Locals;
            //5. *Instructions will be handled in EnterSequence below
            //var seq = Body.Instructions;
            //6.
            context.Assert( context.OpStack.Count >= funcType.ParameterTypes.Arity,
                $"Function invocation failed. Operand Stack underflow.");
            //7.
            context.Assert(_asideVals.Count == 0,
                $"Shared temporary stack had values left in it.");
            context.OpStack.PopResults(funcType.ParameterTypes, ref _asideVals);
            //8.
            //Push the frame and operate on the frame on the stack.
            var frame = context.ReserveFrame(Module, funcType, Index, t);
            // frame.FuncId = wasmFunc.Id;
                
            int li = 0;
            int localCount = funcType.ParameterTypes.Arity + t.Length;
            //Load parameters
            while (_asideVals.Count > 0)
            {
                // frame.Locals.Set((LocalIdx)li, _asideVals.Pop());
                frame.Locals.Data[li] = _asideVals.Pop();
                li += 1;
            }
            //Set the Locals to default
            for (int ti = 0; li < localCount; ++li, ++ti)
            {
                // frame.Locals.Set((LocalIdx)li, new Value(t[ti]));
                frame.Locals.Data[li] = new Value(t[ti]);
            }

            //9.
            context.PushFrame(frame);
            
            //10.
            frame.PushLabel(Body.LabelTarget); 
            
            frame.ReturnLabel.Arity = funcType.ResultType.Arity;
            frame.ReturnLabel.Instruction = LabelInst;
            frame.ReturnLabel.ContinuationAddress = context.GetPointer();
            frame.ReturnLabel.StackHeight = 0;
            
            context.EnterSequence(Body.Instructions);
        }
        
        public override string ToString() => $"FunctionInstance[{Id}] (Type: {Type}, IsExport: {IsExport})";
    }
}