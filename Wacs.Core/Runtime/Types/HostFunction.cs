using System;
using System.Reflection;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a host (native) function instance provided by the host environment.
    /// </summary>
    public class HostFunction : IFunctionInstance
    {
        /// <summary>
        /// The delegate representing the host function implementation.
        /// </summary>
        private readonly Delegate _hostFunction;

        private readonly MethodInfo _invoker;

        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// Initializes a new instance of the <see cref="HostFunction"/> class.
        /// </summary>
        /// <param name="type">The function type.</param>
        /// <param name="delType">The System.Type of the delegate must match type.</param>
        /// <param name="hostFunction">The delegate representing the host function.</param>
        public HostFunction(FunctionType type, Type delType, Delegate hostFunction)
        {
            Type = type;
            _hostFunction = hostFunction;
            _invoker = delType.GetMethod("Invoke")!;
        }

        public FunctionType Type { get; }

        /// <summary>
        /// Invokes the host function with the given arguments.
        /// Pushes any results onto the passed OpStack.
        /// </summary>
        /// <param name="args">The arguments to pass to the function.</param>
        /// <param name="opStack">The Operand Stack to push results onto.</param>
        public void Invoke(object[] args, OpStack opStack)
        {
            var result = _invoker.Invoke(_hostFunction, args);
            if (!Type.ResultType.IsEmpty)
            {
                opStack.PushValue(new Value(result));
                // if (result is Array resultArray)
                // {
                //     foreach (var item in resultArray)
                //     {
                //         opStack.PushValue(new Value(item));
                //     }
                // }
            }
            
        }
    }
}