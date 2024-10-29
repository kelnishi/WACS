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
                RootContextData = { [nameof(WasmValidationContext)] = parent.GetValidationContext() }
            };
    }
}