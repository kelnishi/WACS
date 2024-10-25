using System;

namespace Wacs.Core.Runtime.Types
{
    public class TrapException : Exception
    {
        public TrapException(string message) : base(message)
        {
        }
    }
}