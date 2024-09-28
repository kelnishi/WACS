using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Represents a host (native) function instance provided by the host environment.
    /// </summary>
    public class HostFunctionInstance : FunctionInstance
    {
        /// <summary>
        /// The delegate representing the host function implementation.
        /// </summary>
        private readonly Func<object[], object[]> _hostFunction;

        /// <summary>
        /// Initializes a new instance of the <see cref="HostFunctionInstance"/> class.
        /// </summary>
        /// <param name="type">The function type.</param>
        /// <param name="hostFunction">The delegate representing the host function.</param>
        public HostFunctionInstance(FunctionType type, Func<object[], object[]> hostFunction)
            : base(type, null) // Host functions may not belong to a specific module instance
        {
            _hostFunction = hostFunction;
        }

        /// <summary>
        /// Invokes the host function with the given arguments.
        /// </summary>
        /// <param name="arguments">The arguments to pass to the function.</param>
        /// <returns>The results returned by the function.</returns>
        public override object[] Invoke(object[] arguments)
        {
            return _hostFunction(arguments);
        }
    }
}