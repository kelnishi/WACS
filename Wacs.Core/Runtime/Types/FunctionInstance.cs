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

        public override string ToString() => $"FunctionInstance[{Id}] (Type: {Type}, IsExport: {IsExport})";
    }
}