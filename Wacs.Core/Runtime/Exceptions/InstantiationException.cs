using System;

namespace Wacs.Core.Runtime.Exceptions
{
    public class InstantiationException : Exception
    {
        public InstantiationException(string message) : base(message) {}
    }
}