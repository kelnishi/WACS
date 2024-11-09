namespace Wacs.Core.Runtime.Exceptions
{
    public class InstantiationException : WasmRuntimeException
    {
        public InstantiationException(string message) : base(message) {}
    }
}