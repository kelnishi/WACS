using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// </summary>
    public interface IFunctionInstance
    {
        FunctionType Type { get; }
        public string Id { get; }
        public void SetName(string name);
    }
}