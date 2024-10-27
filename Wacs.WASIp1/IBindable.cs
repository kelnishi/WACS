using Wacs.Core.Runtime;

namespace Wacs.WASIp1
{
    public interface IBindable
    {
        void BindToRuntime(WasmRuntime runtime);
    }
}