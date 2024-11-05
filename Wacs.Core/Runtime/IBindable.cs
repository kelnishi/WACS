namespace Wacs.Core.Runtime
{
    public interface IBindable
    {
        void BindToRuntime(WasmRuntime runtime);
    }
}