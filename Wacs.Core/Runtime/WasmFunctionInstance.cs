using Wacs.Core.Execution;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Represents a WebAssembly-defined function instance.
    /// </summary>
    public class WasmFunctionInstance : FunctionInstance
    {
        /// <summary>
        /// Gets the function definition containing the code and locals.
        /// </summary>
        public Module.FuncLocalsBody Definition { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WasmFunctionInstance"/> class.
        /// </summary>
        /// <param name="type">The function type.</param>
        /// <param name="definition">The function definition.</param>
        /// <param name="module">The module instance containing the function.</param>
        public WasmFunctionInstance(FunctionType type, Module.FuncLocalsBody definition, ModuleInstance module)
            : base(type, module)
        {
            Definition = definition;
        }

        /// <summary>
        /// Invokes the WebAssembly function with the given arguments.
        /// </summary>
        /// <param name="arguments">The arguments to pass to the function.</param>
        /// <returns>The results returned by the function.</returns>
        public override object[] Invoke(object[] arguments)
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
            return null;
        }
    }
}