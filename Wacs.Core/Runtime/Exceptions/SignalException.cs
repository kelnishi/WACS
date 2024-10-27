using System;

namespace Wacs.Core.Runtime.Exceptions
{
    public class SignalException : Exception
    {
        public SignalException(int signal)
            : base($"Wasm process received signal {signal}.")
        {
            Signal = signal;
        }

        public int Signal { get; }
    }
}