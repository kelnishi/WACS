using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a host (native) function instance provided by the host environment.
    /// </summary>
    public class HostFunction : IFunctionInstance
    {
        public delegate object[] HostFunctionDelegate(object[] arguments);
        
        public FunctionType Type { get; }
        
        /// <summary>
        /// The delegate representing the host function implementation.
        /// </summary>
        private readonly HostFunctionDelegate _hostFunction;

        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// Initializes a new instance of the <see cref="HostFunction"/> class.
        /// </summary>
        /// <param name="type">The function type.</param>
        /// <param name="hostFunction">The delegate representing the host function.</param>
        public HostFunction(FunctionType type, HostFunctionDelegate hostFunction)
        {
            Type = type;
            _hostFunction = hostFunction;
        }

        /// <summary>
        /// Invokes the host function with the given arguments.
        /// </summary>
        /// <param name="arguments">The arguments to pass to the function.</param>
        /// <returns>The results returned by the function.</returns>
        public object[] Invoke(object[] arguments)
        {
            return _hostFunction(arguments);
        }
    }
}