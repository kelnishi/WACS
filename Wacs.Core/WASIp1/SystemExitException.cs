namespace Wacs.Core.WASIp1
{
    public class SystemExitException : SignalException
    {
        public SystemExitException(int signal)
        : base(signal, $"Wasi function exited with signal {(SystemExit)signal}") { }

        public SystemExitException(int signal, string message) : base(signal, message) { }

        public SystemExit SystemExit => (SystemExit)Signal;

        public override string HumanReadable => SystemExit.HumanReadable();
    }
}