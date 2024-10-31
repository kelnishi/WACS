using Wacs.Core.Types;

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
        public FunctionInstance(FunctionType type, ModuleInstance module, Module.Function definition)
        {
            Type = type;
            Module = module;
            Definition = definition;
        }

        public ModuleInstance Module { get; }

        /// <summary>
        /// Gets the function definition containing the code and locals.
        /// </summary>
        public Module.Function Definition { get; }

        public string ModuleName => Module.Name;
        private string Name { get; set; } = "";

        public FunctionType Type { get; }
        public void SetName(string value) => Name = value;
        public string Id => string.IsNullOrEmpty(Name)?"":$"{ModuleName}.{Name}";
    }
}