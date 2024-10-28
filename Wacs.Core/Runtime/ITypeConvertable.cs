namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Implement on your valuetype for automatic conversion during host invocation.
    /// </summary>
    public interface ITypeConvertable
    {
        //Convert Wasm numeric value to requested host parameter type
        void FromWasmValue(object wasmValue);

        Value ToWasmType();
    }
}