using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a WebAssembly-defined function instance.
    /// </summary>
    public class FunctionInstance : IFunctionInstance
    {
        public FunctionType Type { get; }
        
        public ModuleInstance Module { get; }

        /// <summary>
        /// Gets the function definition containing the code and locals.
        /// </summary>
        public Module.Function Definition { get; }

        /// <summary>
        /// @Spec 4.5.3.1. Functions
        /// Initializes a new instance of the <see cref="FunctionInstance"/> class.
        /// </summary>
        public FunctionInstance(FunctionType type, ModuleInstance module, Module.Function definition)
        {
            Type = type;
            Module = module;
            Definition = definition;
        }

        /// <summary>
        /// Invokes the WebAssembly function with the given arguments.
        /// </summary>
        /// <param name="arguments">The arguments to pass to the function.</param>
        /// <returns>The results returned by the function.</returns>
        public object[] Invoke(object[] arguments)
        {
            // var context = ExecContext.CreateExecContext(this, arguments);
            //
            // foreach (var instruction in Definition.Body.Instructions)
            // {
            //     instruction.Execute(context);
            // }
            //
            // var results = new object[Type.ResultType.Length];
            // for (int i = results.Length - 1; i >= 0; i--)
            // {
            //     results[i] = context.Stack.Pop();
            // }
            //
            // return results;
            return new object[]{};
        }
    }
}