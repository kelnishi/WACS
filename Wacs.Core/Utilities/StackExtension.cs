using System;
using System.Collections.Generic;

namespace Wacs.Core.Utilities
{
    public static class StackExtension
    {
        public static T PeekAt<T>(this Stack<T> stack, int index)
        {
            if (index < 0 || index >= stack.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Index must be within the bounds of the stack.");
            
            return stack.ToArray()[index];
        }
    }
}