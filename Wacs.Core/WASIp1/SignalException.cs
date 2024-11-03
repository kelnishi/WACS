using System;

namespace Wacs.Core.WASIp1
{
    public class SignalException : Exception
    {
        public SignalException(int signal, string message) : base(message) => Signal = signal;

        public SignalException(int signal)
            : base($"Wasm process received signal {(ErrNo)signal}.") => Signal = signal;

        public int Signal { get; }

        public virtual string HumanReadable => ((ErrNo)Signal).HumanReadable();
    }
}