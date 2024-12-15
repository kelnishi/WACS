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
using FluentValidation;
using FluentValidation.Internal;

namespace Wacs.Core.Validation
{
    public static class ValidationContextExtension
    {
        public static WasmValidationContext GetValidationContext(this IValidationContext context)
            => context.RootContextData.TryGetValue(nameof(WasmValidationContext), out var execContextData) && execContextData is WasmValidationContext execContext
                ? execContext
                : throw new InvalidOperationException($"The WasmValidationContext is not present in the RootContextData.");

        public static PropertyChain Append(this PropertyChain chain, object obj)
        {
            var subchain = new PropertyChain(chain);
            var name = obj.GetType().Name;
            subchain.Add(name);
            return subchain;
        }

        public static PropertyChain AppendIndex(this PropertyChain chain, object obj)
        {
            if ((int)obj == -1)
                return chain;
            
            var subchain = new PropertyChain(chain);
            subchain.AddIndexer(obj);
            return subchain;
        }

        public static ValidationContext<T> GetSubContext<T>(this IValidationContext parent, T child, int index = -1)
            where T : class
            => new(child, parent.PropertyChain.AppendIndex(index).Append(child), new DefaultValidatorSelector())
            {
                RootContextData =
                {
                    [nameof(WasmValidationContext)] = parent.GetValidationContext(),
                    ["Index"] = index
                }
            };
    }
}