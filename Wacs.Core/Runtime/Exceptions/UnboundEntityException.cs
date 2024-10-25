using System;

namespace Wacs.Core.Runtime.Exceptions
{
    public class UnboundEntityException : Exception
    {
        public UnboundEntityException(string s) : base(s) { }
    }
}