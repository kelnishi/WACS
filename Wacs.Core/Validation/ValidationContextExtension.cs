using System;
using FluentValidation;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Validation
{
    public static class ValidationContextExtension
    {
        public static WasmValidationContext GetValidationContext<T>(this ValidationContext<T> context)
            where T : class
            => context.RootContextData.TryGetValue(nameof(WasmValidationContext), out var execContextData) && execContextData is WasmValidationContext execContext
                ? execContext
                : throw new InvalidOperationException($"The WasmValidationContext is not present in the RootContextData.");

        public static ValidationContext<T> GetSubContext<TP, T>(this ValidationContext<TP> parent, T child)
            where T : class
            where TP : class
            => new(child)
            {
                RootContextData =
                {
                    [nameof(WasmValidationContext)] = parent.GetValidationContext()
                }
            };
    }
}