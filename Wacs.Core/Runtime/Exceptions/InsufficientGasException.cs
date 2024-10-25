using System;

namespace Wacs.Core.Runtime.Exceptions
{
    public class InsufficientGasException : Exception
    {
        public InsufficientGasException(string s) : base(s) {}
    }
}