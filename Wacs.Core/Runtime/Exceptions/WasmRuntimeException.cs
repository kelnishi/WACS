using System;

namespace Wacs.Core.Runtime.Exceptions
{
    public class WasmRuntimeException : Exception
    {
        public WasmRuntimeException(string message) : base(message) { }
    }
}